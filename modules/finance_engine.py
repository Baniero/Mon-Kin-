from datetime import datetime


def _payment_status(amount_due, paid_total):
    if amount_due <= 0:
        return "non_paye"
    if paid_total >= amount_due:
        return "paye"
    if paid_total > 0:
        return "partiel"
    return "non_paye"


def _insert_ledger(cur, patient_id, appointment_id, entry_type, amount, reference="", note=""):
    cur.execute(
        """
        INSERT INTO finance_ledger(patient_id, appointment_id, entry_type, amount, reference, note)
        VALUES (?, ?, ?, ?, ?, ?)
        """,
        (patient_id, appointment_id, entry_type, float(amount or 0), reference or "", note or ""),
    )


def register_advance_credit(cur, patient_id, amount, note="Avance patient"):
    amount = float(amount or 0)
    if amount <= 0:
        return

    cur.execute(
        "INSERT INTO advance_transactions(patient_id, amount, note) VALUES (?, ?, ?)",
        (patient_id, amount, note),
    )
    transaction_id = int(cur.lastrowid)

    cur.execute(
        "INSERT INTO advance_lots(patient_id, transaction_id, original_amount, remaining_amount) VALUES (?, ?, ?, ?)",
        (patient_id, transaction_id, amount, amount),
    )

    _insert_ledger(cur, patient_id, None, "credit_avance", amount, str(transaction_id), note)


def reset_appointment_fifo_usage(cur, appointment_id, patient_id):
    cur.execute(
        "SELECT lot_id, IFNULL(amount_used, 0) FROM advance_lot_usage WHERE appointment_id=? ORDER BY id",
        (appointment_id,),
    )
    usages = cur.fetchall()
    if usages:
        for lot_id, amount_used in usages:
            used = float(amount_used or 0)
            if used <= 0:
                continue
            cur.execute(
                "UPDATE advance_lots SET remaining_amount = remaining_amount + ? WHERE id=?",
                (used, int(lot_id)),
            )
        cur.execute("DELETE FROM advance_lot_usage WHERE appointment_id=?", (appointment_id,))

    cur.execute("SELECT IFNULL(amount_used, 0) FROM advance_usage WHERE appointment_id=?", (appointment_id,))
    row = cur.fetchone()
    prev = float(row[0] or 0) if row else 0.0
    if prev > 0:
        cur.execute(
            "UPDATE patient_finance SET advance_balance = IFNULL(advance_balance, 0) + ?, updated_at=CURRENT_TIMESTAMP WHERE patient_id=?",
            (prev, patient_id),
        )
        cur.execute("DELETE FROM advance_usage WHERE appointment_id=?", (appointment_id,))


def apply_payment_with_fifo(cur, appointment_id, status, manual_paid, reason="manuel"):
    cur.execute(
        "SELECT patient_id, IFNULL(amount, 0), IFNULL(paid_amount, 0), IFNULL(payment_status, 'non_paye') FROM appointments WHERE id=?",
        (appointment_id,),
    )
    row = cur.fetchone()
    if not row:
        return "non_paye", 0.0

    patient_id, amount_due, old_paid, old_status = row
    patient_id = int(patient_id)
    amount_due = float(amount_due or 0)
    old_paid = float(old_paid or 0)
    manual_paid = float(manual_paid or 0)

    cur.execute(
        "INSERT OR IGNORE INTO patient_finance(patient_id, session_price, patient_share, cnam_share, advance_balance, total_advance_paid) VALUES (?, 0, 0, 0, 0, 0)",
        (patient_id,),
    )

    # Revert previous allocations for deterministic recomputation.
    reset_appointment_fifo_usage(cur, appointment_id, patient_id)

    paid_total = max(0.0, manual_paid)
    fifo_used_total = 0.0

    if status in ("present", "effectue") and paid_total < amount_due:
        remaining_to_cover = max(0.0, amount_due - paid_total)
        if remaining_to_cover > 0:
            cur.execute(
                """
                SELECT id, IFNULL(remaining_amount, 0)
                FROM advance_lots
                WHERE patient_id=? AND IFNULL(remaining_amount, 0) > 0
                ORDER BY created_at, id
                """,
                (patient_id,),
            )
            for lot_id, remaining in cur.fetchall():
                lot_remaining = float(remaining or 0)
                if lot_remaining <= 0 or remaining_to_cover <= 0:
                    continue
                to_use = min(lot_remaining, remaining_to_cover)
                remaining_to_cover -= to_use
                fifo_used_total += to_use
                paid_total += to_use

                cur.execute(
                    "UPDATE advance_lots SET remaining_amount = remaining_amount - ? WHERE id=?",
                    (to_use, int(lot_id)),
                )
                cur.execute(
                    "INSERT INTO advance_lot_usage(lot_id, appointment_id, amount_used) VALUES (?, ?, ?)",
                    (int(lot_id), appointment_id, to_use),
                )

    if fifo_used_total > 0:
        cur.execute(
            "UPDATE patient_finance SET advance_balance = IFNULL(advance_balance, 0) - ?, updated_at=CURRENT_TIMESTAMP WHERE patient_id=?",
            (fifo_used_total, patient_id),
        )
        cur.execute(
            "INSERT INTO advance_usage(appointment_id, patient_id, amount_used) VALUES (?, ?, ?)",
            (appointment_id, patient_id, fifo_used_total),
        )
        _insert_ledger(cur, patient_id, appointment_id, "usage_avance", fifo_used_total, str(appointment_id), "Consommation FIFO")

    new_status = _payment_status(amount_due, paid_total)

    cur.execute(
        "UPDATE appointments SET status=?, payment_status=?, paid_amount=? WHERE id=?",
        (status, new_status, paid_total, appointment_id),
    )

    # Keep a debit entry per appointment and optional direct payment entry.
    cur.execute(
        "SELECT COUNT(*) FROM finance_ledger WHERE appointment_id=? AND entry_type='debit_seance'",
        (appointment_id,),
    )
    if int(cur.fetchone()[0] or 0) == 0 and amount_due > 0:
        _insert_ledger(cur, patient_id, appointment_id, "debit_seance", amount_due, str(appointment_id), "Debit seance")

    direct_paid = max(0.0, paid_total - fifo_used_total)
    old_direct_paid = max(0.0, old_paid)
    if direct_paid != old_direct_paid:
        delta = direct_paid - old_direct_paid
        if abs(delta) > 0.0001:
            entry_type = "paiement_direct" if delta > 0 else "ajustement_paiement"
            _insert_ledger(cur, patient_id, appointment_id, entry_type, abs(delta), str(appointment_id), reason)

    cur.execute(
        """
        INSERT INTO payment_audit(appointment_id, patient_id, old_paid, new_paid, old_status, new_status, reason)
        VALUES (?, ?, ?, ?, ?, ?, ?)
        """,
        (
            appointment_id,
            patient_id,
            old_paid,
            paid_total,
            old_status,
            new_status,
            reason,
        ),
    )

    return new_status, paid_total
