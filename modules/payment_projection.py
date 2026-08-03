def _compute_status(amount_due, paid_total):
    if amount_due <= 0:
        return "non_paye"
    if paid_total >= amount_due:
        return "paye"
    if paid_total > 0:
        return "partiel"
    return "non_paye"


def project_patient_payment_states(cur, patient_id):
    cur.execute(
        "SELECT IFNULL(advance_balance, 0) FROM patient_finance WHERE patient_id=?",
        (patient_id,),
    )
    row = cur.fetchone()
    available_advance = float(row[0] or 0) if row else 0.0

    cur.execute(
        """
        SELECT id,
               IFNULL(amount, 0),
               IFNULL(paid_amount, 0),
               IFNULL(payment_status, 'non_paye')
        FROM appointments
        WHERE patient_id=?
        ORDER BY start_datetime, id
        """,
        (patient_id,),
    )
    appts = cur.fetchall()

    projections = {}
    for appointment_id, amount, paid_amount, current_status in appts:
        amount_due = float(amount or 0)
        base_paid = float(paid_amount or 0)
        remaining = max(0.0, amount_due - base_paid)

        projected_use = 0.0
        if remaining > 0 and available_advance > 0:
            projected_use = min(available_advance, remaining)
            available_advance -= projected_use

        projected_paid = base_paid + projected_use
        projected_status = _compute_status(amount_due, projected_paid)

        projections[int(appointment_id)] = {
            "paid_total": projected_paid,
            "payment_status": projected_status,
            "projected_advance": projected_use,
            "has_projection": projected_use > 0,
            "source_status": (current_status or "non_paye").strip(),
        }

    return projections
