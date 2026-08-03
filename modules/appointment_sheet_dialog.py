import sqlite3
from datetime import datetime, timedelta

from PyQt6.QtCore import QDate, QTime
from PyQt6.QtWidgets import (
    QDialog,
    QVBoxLayout,
    QTabWidget,
    QWidget,
    QFormLayout,
    QLabel,
    QDateEdit,
    QTimeEdit,
    QSpinBox,
    QHBoxLayout,
    QPushButton,
    QMessageBox,
    QComboBox,
    QLineEdit,
    QInputDialog,
)

from modules.finance_engine import apply_payment_with_fifo
from modules.payment_projection import project_patient_payment_states


class AppointmentSheetDialog(QDialog):
    def __init__(self, db_path, appointment_id, parent=None):
        super().__init__(parent)
        self.db_path = db_path
        self.appointment_id = int(appointment_id)
        self._data = None

        self.setWindowTitle("Fiche seance")
        self.setMinimumWidth(640)
        self.setMinimumHeight(520)

        self._load_data()
        self._build_ui()

    def _db(self):
        return sqlite3.connect(self.db_path)

    def _load_data(self):
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT a.id,
                   a.patient_id,
                   a.kine_id,
                   IFNULL(u.full_name, u.username),
                   a.start_datetime,
                   IFNULL(a.end_datetime, ''),
                   IFNULL(a.acte, ''),
                   IFNULL(a.status, 'planifie'),
                   IFNULL(a.payment_status, 'non_paye'),
                   IFNULL(a.amount, 0),
                   IFNULL(a.paid_amount, 0),
                   IFNULL(a.cnam_covered, 0),
                   IFNULL(a.notes, ''),
                   IFNULL(a.room, ''),
                   IFNULL(p.code_patient, ''),
                   p.nom,
                   IFNULL(p.prenom, ''),
                   IFNULL(p.date_naissance, ''),
                   IFNULL(p.sexe, ''),
                   IFNULL(p.telephone1, ''),
                   IFNULL(p.telephone2, ''),
                   IFNULL(p.adresse, ''),
                   IFNULL(p.couverture, ''),
                   IFNULL(m.diagnostic, ''),
                   IFNULL(m.medecin_traitant, ''),
                   IFNULL(m.nature_seances, ''),
                   IFNULL(m.nb_seances_programme, 0),
                   IFNULL(m.objectifs, ''),
                   IFNULL(m.remarques, ''),
                   IFNULL(f.advance_balance, 0),
                   IFNULL(f.total_advance_paid, 0)
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            LEFT JOIN users u ON u.id = a.kine_id
            LEFT JOIN medical_records m ON m.patient_id = p.id
            LEFT JOIN patient_finance f ON f.patient_id = p.id
            WHERE a.id=?
            """,
            (self.appointment_id,),
        )
        row = cur.fetchone()
        projection_map = project_patient_payment_states(cur, row[1]) if row else {}
        conn.close()
        if not row:
            raise ValueError("Appointment not found")

        projection = projection_map.get(int(row[0]), {})
        projected_paid = float(projection.get("paid_total", row[10] or 0) or 0)
        projected_status = projection.get("payment_status", row[8] or "non_paye")
        status_label = projected_status
        if projection.get("has_projection"):
            status_label = f"{projected_status} (avance prelevee)"

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            "SELECT alert_type, severity, content FROM patient_alerts WHERE patient_id=? AND IFNULL(active, 1)=1 ORDER BY id DESC",
            (row[1],),
        )
        self._active_alerts = cur.fetchall()
        conn.close()

        self._data = {
            "appointment_id": row[0],
            "patient_id": row[1],
            "kine_id": row[2],
            "kine": row[3] or "",
            "start": row[4],
            "end": row[5] or "",
            "acte": row[6] or "",
            "status": row[7] or "planifie",
            "payment_status": status_label,
            "amount": float(row[9] or 0),
            "paid_amount": projected_paid,
            "cnam": float(row[11] or 0),
            "notes": row[12] or "",
            "room": row[13] or "",
            "code": row[14] or "",
            "nom": row[15] or "",
            "prenom": row[16] or "",
            "naissance": row[17] or "",
            "sexe": row[18] or "",
            "tel1": row[19] or "",
            "tel2": row[20] or "",
            "adresse": row[21] or "",
            "couverture": row[22] or "",
            "diagnostic": row[23] or "",
            "medecin": row[24] or "",
            "nature": row[25] or "",
            "nb_seances": int(row[26] or 0),
            "objectifs": row[27] or "",
            "remarques": row[28] or "",
            "advance_balance": float(row[29] or 0),
            "total_advance": float(row[30] or 0),
        }

    def _build_readonly_form(self, entries):
        widget = QWidget()
        form = QFormLayout(widget)
        for label, value in entries:
            v = QLabel(str(value))
            v.setWordWrap(True)
            form.addRow(label, v)
        return widget

    def _build_ui(self):
        root = QVBoxLayout(self)

        tabs = QTabWidget()
        root.addWidget(tabs)

        patient_entries = [
            ("Code patient", self._data["code"]),
            ("Nom", self._data["nom"]),
            ("Prenom", self._data["prenom"]),
            ("Date naissance", self._data["naissance"]),
            ("Sexe", self._data["sexe"]),
            ("Telephone 1", self._data["tel1"]),
            ("Telephone 2", self._data["tel2"]),
            ("Adresse", self._data["adresse"]),
            ("Couverture", self._data["couverture"]),
        ]
        tabs.addTab(self._build_readonly_form(patient_entries), "Fiche patient")

        if self._active_alerts:
            alert_entries = []
            for alert_type, severity, content in self._active_alerts:
                alert_entries.append((f"{alert_type} ({severity})", content))
            tabs.addTab(self._build_readonly_form(alert_entries), "Alertes cliniques")

        general_entries = [
            ("Date/heure debut", self._data["start"]),
            ("Date/heure fin", self._data["end"]),
            ("Kine", self._data["kine"]),
            ("Acte", self._data["acte"]),
            ("Statut", self._data["status"]),
            ("Diagnostic", self._data["diagnostic"]),
            ("Medecin traitant", self._data["medecin"]),
            ("Nature seances", self._data["nature"]),
            ("Nb seances programme", self._data["nb_seances"]),
        ]
        tabs.addTab(self._build_readonly_form(general_entries), "Donnees generales")

        other_entries = [
            ("Objectifs", self._data["objectifs"]),
            ("Remarques", self._data["remarques"]),
            ("Notes seance", self._data["notes"]),
        ]
        tabs.addTab(self._build_readonly_form(other_entries), "Autres donnees")

        payment_entries = [
            ("Etat paiement", self._data["payment_status"]),
            ("Montant seance", f"{self._data['amount']:.2f}"),
            ("Montant paye", f"{self._data['paid_amount']:.2f}"),
            ("Part CNAM", f"{self._data['cnam']:.2f}"),
            ("Solde avance", f"{self._data['advance_balance']:.2f}"),
            ("Total avances", f"{self._data['total_advance']:.2f}"),
        ]
        tabs.addTab(self._build_readonly_form(payment_entries), "Etat paiement")

        replanning = QWidget()
        replanning_form = QFormLayout(replanning)
        self.date_edit = QDateEdit()
        self.date_edit.setCalendarPopup(True)
        self.time_edit = QTimeEdit()
        self.duration_edit = QSpinBox()
        self.duration_edit.setRange(10, 300)
        self.duration_edit.setSingleStep(5)
        self.kine_combo = QComboBox()
        self.room_edit = QLineEdit()
        self.room_edit.setText(self._data["room"])

        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT id, IFNULL(full_name, username) FROM users WHERE role IN ('kine','admin') AND IFNULL(active,1)=1 ORDER BY full_name")
        for kid, label in cur.fetchall():
            self.kine_combo.addItem(label, int(kid))
        conn.close()
        for idx in range(self.kine_combo.count()):
            if self.kine_combo.itemData(idx) == int(self._data["kine_id"] or 0):
                self.kine_combo.setCurrentIndex(idx)
                break

        start_dt = datetime.strptime(self._data["start"], "%Y-%m-%d %H:%M:%S")
        self.date_edit.setDate(QDate(start_dt.year, start_dt.month, start_dt.day))
        self.time_edit.setTime(QTime(start_dt.hour, start_dt.minute))
        if self._data["end"]:
            end_dt = datetime.strptime(self._data["end"], "%Y-%m-%d %H:%M:%S")
            duration = max(10, int((end_dt - start_dt).total_seconds() / 60))
        else:
            duration = 30
        self.duration_edit.setValue(duration)

        replanning_form.addRow("Nouvelle date", self.date_edit)
        replanning_form.addRow("Nouvelle heure", self.time_edit)
        replanning_form.addRow("Duree (min)", self.duration_edit)
        replanning_form.addRow("Kine", self.kine_combo)
        replanning_form.addRow("Salle", self.room_edit)
        tabs.addTab(replanning, "Changer date/heure")

        btns = QHBoxLayout()
        self.presence_btn = QPushButton("Présence")
        self.presence_btn.clicked.connect(self._toggle_presence)
        self.payment_btn = QPushButton("Paiement")
        self.payment_btn.clicked.connect(self._manage_payment)
        save_btn = QPushButton("Enregistrer date/heure")
        save_btn.clicked.connect(self._save_datetime)
        close_btn = QPushButton("Fermer")
        close_btn.clicked.connect(self.reject)
        btns.addWidget(self.presence_btn)
        btns.addWidget(self.payment_btn)
        btns.addWidget(save_btn)
        btns.addWidget(close_btn)
        root.addLayout(btns)

    def _save_datetime(self):
        picked = self.date_edit.date()
        picked_time = self.time_edit.time()
        start_dt = datetime(
            picked.year(),
            picked.month(),
            picked.day(),
            picked_time.hour(),
            picked_time.minute(),
        )
        end_dt = start_dt + timedelta(minutes=int(self.duration_edit.value()))

        conn = self._db()
        cur = conn.cursor()
        selected_kine_id = self.kine_combo.currentData()
        cur.execute(
            """
            SELECT COUNT(*)
            FROM appointments
            WHERE kine_id=?
              AND start_datetime=?
              AND id<>?
            """,
            (selected_kine_id, start_dt.strftime("%Y-%m-%d %H:%M:%S"), self.appointment_id),
        )
        collision = int(cur.fetchone()[0] or 0)
        if collision > 0:
            conn.close()
            QMessageBox.warning(self, "Conflit", "Le kine a deja une seance a cette date/heure.")
            return

        cur.execute(
            """
            UPDATE appointments
            SET start_datetime=?, end_datetime=?, kine_id=?, room=?
            WHERE id=?
            """,
            (
                start_dt.strftime("%Y-%m-%d %H:%M:%S"),
                end_dt.strftime("%Y-%m-%d %H:%M:%S"),
                selected_kine_id,
                self.room_edit.text().strip(),
                self.appointment_id,
            ),
        )
        conn.commit()
        conn.close()
        QMessageBox.information(self, "Succes", "Date et heure mises a jour.")
        self.accept()

    def _toggle_presence(self):
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT IFNULL(status, 'planifie'), IFNULL(paid_amount, 0) FROM appointments WHERE id=?", (self.appointment_id,))
        row = cur.fetchone()
        if not row:
            conn.close()
            return
        current_status, paid_amount = row
        new_status = "present" if current_status not in ("present", "effectue") else "absent"
        apply_payment_with_fifo(cur, self.appointment_id, new_status, float(paid_amount or 0), reason="fiche_seance")
        conn.commit()
        conn.close()
        QMessageBox.information(self, "Succes", f"Statut présence mis à jour: {new_status}.")
        self.accept()

    def _manage_payment(self):
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT IFNULL(paid_amount, 0), IFNULL(status, 'planifie') FROM appointments WHERE id=?", (self.appointment_id,))
        row = cur.fetchone()
        if not row:
            conn.close()
            return
        current_paid, status = float(row[0] or 0), row[1]
        amount, ok = QInputDialog.getDouble(
            self,
            "Paiement séance",
            "Montant payé (DT):",
            current_paid,
            0,
            100000,
            2,
        )
        if not ok:
            conn.close()
            return
        apply_payment_with_fifo(cur, self.appointment_id, status, float(amount), reason="paiement_fiche_seance")
        conn.commit()
        conn.close()
        QMessageBox.information(self, "Succes", "Paiement mis à jour.")
        self.accept()
