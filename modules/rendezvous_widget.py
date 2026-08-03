import sqlite3
from datetime import datetime, timedelta

from PyQt6.QtCore import QDate, QTime, Qt
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QTabWidget, QLabel, QDateEdit,
    QTableWidget, QTableWidgetItem, QPushButton, QDialog, QFormLayout,
    QComboBox, QTimeEdit, QSpinBox, QDoubleSpinBox, QMessageBox, QCalendarWidget,
    QListWidget, QFileDialog, QHeaderView, QScrollBar, QInputDialog,
    QLineEdit, QAbstractItemView
)

from modules.export_utils import export_simple_table_pdf
from modules.appointment_sheet_dialog import AppointmentSheetDialog
from modules.finance_engine import apply_payment_with_fifo


class AppointmentDialog(QDialog):
    def __init__(self, db_path, selected_date, parent=None):
        super().__init__(parent)
        self.db_path = db_path
        self.selected_date = selected_date
        self.setWindowTitle("Programmer une séance")
        self.setMinimumWidth(420)

        layout = QFormLayout(self)

        self.patient_combo = QComboBox()
        self.kine_combo = QComboBox()
        self.acte_combo = QComboBox()
        self.acte_combo.setEditable(True)
        self.time_edit = QTimeEdit(QTime(8, 0))
        self.duration_spin = QSpinBox()
        self.duration_spin.setRange(15, 180)
        self.duration_spin.setSingleStep(15)
        self.duration_spin.setValue(30)
        self.amount_spin = QDoubleSpinBox()
        self.amount_spin.setRange(0, 10000)
        self.amount_spin.setValue(30)
        self.cnam_spin = QDoubleSpinBox()
        self.cnam_spin.setRange(0, 10000)
        self.room_input = QLineEdit()
        self.room_input.setPlaceholderText("Ex: Salle 1")
        self._patient_prices = {}
        self._patient_cnam_share = {}
        self._patient_duration = {}

        layout.addRow("Patient", self.patient_combo)
        layout.addRow("Kiné", self.kine_combo)
        layout.addRow("Nature de l'acte", self.acte_combo)
        layout.addRow("Heure", self.time_edit)
        layout.addRow("Durée (minutes)", self.duration_spin)
        layout.addRow("Montant séance", self.amount_spin)
        layout.addRow("Part CNAM", self.cnam_spin)
        layout.addRow("Salle", self.room_input)

        buttons = QHBoxLayout()
        ok = QPushButton("Enregistrer")
        cancel = QPushButton("Annuler")
        ok.clicked.connect(self.accept)
        cancel.clicked.connect(self.reject)
        buttons.addWidget(ok)
        buttons.addWidget(cancel)
        layout.addRow(buttons)

        self._load_data()
        self.patient_combo.currentIndexChanged.connect(self._on_patient_changed)

    def _load_data(self):
        conn = sqlite3.connect(self.db_path)
        cur = conn.cursor()

        cur.execute("SELECT id, nom || ' ' || IFNULL(prenom, '') FROM patients ORDER BY nom")
        for pid, label in cur.fetchall():
            self.patient_combo.addItem(label.strip(), pid)
            cur.execute(
                "SELECT IFNULL(patient_share, IFNULL(session_price, 0)), IFNULL(cnam_share, 0) FROM patient_finance WHERE patient_id=?",
                (pid,),
            )
            row = cur.fetchone()
            if row:
                self._patient_prices[pid] = float(row[0] or 0)
                self._patient_cnam_share[pid] = float(row[1] or 0)
            else:
                self._patient_prices[pid] = 0.0
                self._patient_cnam_share[pid] = 0.0
            cur.execute("SELECT IFNULL(duree_seance_minutes, 30) FROM medical_records WHERE patient_id=?", (pid,))
            row_dur = cur.fetchone()
            self._patient_duration[pid] = int(row_dur[0] or 30) if row_dur else 30

        cur.execute("SELECT id, IFNULL(full_name, username) FROM users WHERE role='kine' AND active=1 ORDER BY full_name")
        kines = cur.fetchall()
        if not kines:
            cur.execute("SELECT id, IFNULL(full_name, username) FROM users WHERE role='admin' AND active=1")
            kines = cur.fetchall()
        for kid, label in kines:
            self.kine_combo.addItem(label.strip(), kid)

        cur.execute("SELECT libelle FROM session_types ORDER BY libelle")
        for (libelle,) in cur.fetchall():
            self.acte_combo.addItem(libelle)

        conn.close()
        self._on_patient_changed()

    def _on_patient_changed(self):
        pid = self.patient_combo.currentData()
        if pid is None:
            return
        price = float(self._patient_prices.get(pid, 0) or 0)
        cnam_share = float(self._patient_cnam_share.get(pid, 0) or 0)
        duration = int(self._patient_duration.get(pid, 30) or 30)
        self.amount_spin.setValue(price)
        self.cnam_spin.setValue(cnam_share)
        self.duration_spin.setValue(duration)

    def get_payload(self):
        start_time = self.time_edit.time()
        start_dt = datetime(
            self.selected_date.year(),
            self.selected_date.month(),
            self.selected_date.day(),
            start_time.hour(),
            start_time.minute(),
        )
        end_dt = start_dt + timedelta(minutes=int(self.duration_spin.value()))

        return {
            "patient_id": self.patient_combo.currentData(),
            "kine_id": self.kine_combo.currentData(),
            "acte": self.acte_combo.currentText().strip(),
            "start": start_dt.strftime("%Y-%m-%d %H:%M:%S"),
            "end": end_dt.strftime("%Y-%m-%d %H:%M:%S"),
            "amount": float(self.amount_spin.value()),
            "cnam": float(self.cnam_spin.value()),
            "room": self.room_input.text().strip(),
        }


class RendezVousWidget(QWidget):
    def __init__(self, db_path):
        super().__init__()
        self.db_path = db_path
        self._build_ui()
        self.refresh()

    def _build_ui(self):
        root = QVBoxLayout(self)
        title = QLabel("Rendez-vous et planning")
        title.setObjectName("sectionTitle")
        root.addWidget(title)

        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        self._build_fix_tab()
        self._build_week_tab()
        self._build_day_tab()
        self._build_month_tab()
        self._build_load_tab()

    def apply_permissions(self, permissions):
        permissions = permissions or {}
        mapping = {
            "rendezvous.fix": 0,
            "rendezvous.week": 1,
            "rendezvous.day": 2,
            "rendezvous.month": 3,
            "rendezvous.charge": 4,
        }
        for key, tab_index in mapping.items():
            if key in permissions:
                self.tabs.setTabVisible(tab_index, bool(permissions[key]))

    def _decorate_table(self, table):
        table.setAlternatingRowColors(True)
        table.verticalHeader().setVisible(False)
        table.setShowGrid(True)
        table.setGridStyle(Qt.PenStyle.SolidLine)
        table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        table.horizontalHeader().setStretchLastSection(True)
        table.setStyleSheet(
            "QTableView { gridline-color: #9AA5B1; }"
        )

    def _build_fix_tab(self):
        self.fix_widget = QWidget()
        layout = QVBoxLayout(self.fix_widget)

        controls = QHBoxLayout()
        controls.addWidget(QLabel("Patient"))
        self.fix_patient_combo = QComboBox()
        self.fix_patient_combo.currentIndexChanged.connect(self._on_fix_patient_changed)
        controls.addWidget(self.fix_patient_combo)

        controls.addWidget(QLabel("Début"))
        self.fix_start_date = QDateEdit(QDate.currentDate())
        self.fix_start_date.setCalendarPopup(True)
        controls.addWidget(self.fix_start_date)

        controls.addWidget(QLabel("Mode"))
        self.fix_mode_combo = QComboBox()
        self.fix_mode_combo.addItems([
            "Journalier",
            "2 jours par semaine",
            "3 séances espacées",
        ])
        controls.addWidget(self.fix_mode_combo)

        controls.addWidget(QLabel("Heure"))
        self.fix_time = QTimeEdit(QTime(8, 0))
        controls.addWidget(self.fix_time)

        controls.addWidget(QLabel("Durée"))
        self.fix_duration = QSpinBox()
        self.fix_duration.setRange(15, 180)
        self.fix_duration.setSingleStep(15)
        self.fix_duration.setValue(30)
        controls.addWidget(self.fix_duration)

        controls.addWidget(QLabel("Kiné"))
        self.fix_kine_combo = QComboBox()
        controls.addWidget(self.fix_kine_combo)

        controls.addWidget(QLabel("Nb à planifier"))
        self.fix_nb_spin = QSpinBox()
        self.fix_nb_spin.setRange(1, 500)
        controls.addWidget(self.fix_nb_spin)
        controls.addStretch()

        buttons = QHBoxLayout()
        generate_btn = QPushButton("Fixer automatiquement")
        generate_btn.clicked.connect(self.generate_smart_appointments)
        buttons.addWidget(generate_btn)

        refresh_btn = QPushButton("Actualiser")
        refresh_btn.clicked.connect(self.refresh_fix_tab)
        buttons.addWidget(refresh_btn)

        delete_program_btn = QPushButton("Supprimer programme")
        delete_program_btn.clicked.connect(self.delete_program_from_fixation)
        buttons.addWidget(delete_program_btn)

        balance_btn = QPushButton("Equilibrage auto semaine")
        balance_btn.clicked.connect(self.balance_weekly_load)
        buttons.addWidget(balance_btn)
        buttons.addStretch()

        layout.addLayout(controls)
        layout.addLayout(buttons)

        self.fix_info_label = QLabel("Séances restantes à programmer")
        layout.addWidget(self.fix_info_label)

        self.fix_table = QTableWidget(0, 5)
        self.fix_table.setHorizontalHeaderLabels([
            "Patient", "Nature", "Séances prévues", "Déjà programmées", "Restantes"
        ])
        self._decorate_table(self.fix_table)
        layout.addWidget(self.fix_table)

        self.tabs.addTab(self.fix_widget, "Fixation RDV")

    def _build_week_tab(self):
        self.week_widget = QWidget()
        layout = QVBoxLayout(self.week_widget)

        row = QHBoxLayout()
        row.addWidget(QLabel("Semaine du"))
        self.week_date = QDateEdit(QDate.currentDate())
        self.week_date.setCalendarPopup(True)
        self.week_date.dateChanged.connect(self.load_week)
        row.addWidget(self.week_date)

        self.week_move_mode_btn = QPushButton("Mode deplacement")
        self.week_move_mode_btn.setCheckable(True)
        row.addWidget(self.week_move_mode_btn)
        row.addStretch()
        layout.addLayout(row)

        self.week_table = QTableWidget(11, 8)
        self.week_table.setHorizontalHeaderLabels([
            "Heure", "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi", "Dimanche"
        ])
        for i, hour in enumerate(range(8, 19)):
            self.week_table.setItem(i, 0, QTableWidgetItem(f"{hour:02d}:00"))
        self._decorate_table(self.week_table)
        self.week_table.itemDoubleClicked.connect(self._on_week_item_double_clicked)
        self.week_table.cellClicked.connect(self._on_week_cell_clicked)
        self.week_table.setWordWrap(True)
        self.week_table.setTextElideMode(Qt.TextElideMode.ElideNone)
        for i in range(self.week_table.rowCount()):
            self.week_table.setRowHeight(i, 86)

        week_row = QHBoxLayout()
        self.week_left_scroll = QScrollBar(Qt.Orientation.Vertical)
        self.week_left_scroll.setFixedWidth(14)
        week_row.addWidget(self.week_left_scroll)
        week_row.addWidget(self.week_table, 1)
        layout.addLayout(week_row)

        # Keep a visible left scrollbar synchronized with the table vertical scrollbar.
        self.week_table.verticalScrollBar().rangeChanged.connect(self.week_left_scroll.setRange)
        self.week_table.verticalScrollBar().valueChanged.connect(self.week_left_scroll.setValue)
        self.week_left_scroll.valueChanged.connect(self.week_table.verticalScrollBar().setValue)

        self.tabs.addTab(self.week_widget, "Planning hebdomadaire")

    def _build_day_tab(self):
        self.day_widget = QWidget()
        layout = QVBoxLayout(self.day_widget)

        top = QHBoxLayout()
        top.addWidget(QLabel("Date"))
        self.day_date = QDateEdit(QDate.currentDate())
        self.day_date.setCalendarPopup(True)
        self.day_date.dateChanged.connect(self.load_day)
        top.addWidget(self.day_date)

        add_btn = QPushButton("Programmer une séance")
        add_btn.clicked.connect(self.add_appointment)
        top.addWidget(add_btn)

        save_btn = QPushButton("Enregistrer présence/paiement")
        save_btn.clicked.connect(self.save_day_statuses)
        top.addWidget(save_btn)

        export_btn = QPushButton("Exporter PDF")
        export_btn.clicked.connect(self.export_day_pdf)
        top.addWidget(export_btn)
        top.addStretch()

        layout.addLayout(top)

        self.day_table = QTableWidget(0, 11)
        self.day_table.setHorizontalHeaderLabels([
            "ID", "Heure", "Patient", "Kiné", "Acte", "Présence", "Paiement", "Montant", "Payé", "Action présence", "Action paiement"
        ])
        self.day_table.setColumnHidden(0, True)
        self._decorate_table(self.day_table)
        self.day_table.itemDoubleClicked.connect(self._on_day_item_double_clicked)
        layout.addWidget(self.day_table)

        self.tabs.addTab(self.day_widget, "Planning journalier")

    def _build_month_tab(self):
        self.month_widget = QWidget()
        layout = QVBoxLayout(self.month_widget)

        self.calendar = QCalendarWidget()
        self.calendar.selectionChanged.connect(self.load_month_day_details)
        layout.addWidget(self.calendar)

        self.month_list = QListWidget()
        layout.addWidget(self.month_list)

        self.tabs.addTab(self.month_widget, "Calendrier mensuel")

    def _build_load_tab(self):
        self.load_widget = QWidget()
        layout = QVBoxLayout(self.load_widget)
        self.load_info_label = QLabel("Charge hebdomadaire par kine")
        layout.addWidget(self.load_info_label)
        self.load_table = QTableWidget(0, 3)
        self.load_table.setHorizontalHeaderLabels(["Kine", "Nb seances", "Duree totale (min)"])
        self._decorate_table(self.load_table)
        layout.addWidget(self.load_table)
        self.tabs.addTab(self.load_widget, "Charge kine")

    def _reset_week_spans(self):
        for r in range(self.week_table.rowCount()):
            for c in range(1, self.week_table.columnCount()):
                self.week_table.setSpan(r, c, 1, 1)

    def _merge_week_vertical_same_programs(self):
        # Merge vertically adjacent cells that contain exactly the same program text.
        for c in range(1, self.week_table.columnCount()):
            r = 0
            while r < self.week_table.rowCount():
                item = self.week_table.item(r, c)
                text = item.text().strip() if item else ""
                if not text:
                    r += 1
                    continue

                start = r
                end = r
                while end + 1 < self.week_table.rowCount():
                    next_item = self.week_table.item(end + 1, c)
                    next_text = next_item.text().strip() if next_item else ""
                    if next_text != text:
                        break
                    end += 1

                span_len = end - start + 1
                if span_len > 1:
                    self.week_table.setSpan(start, c, span_len, 1)
                r = end + 1

    def _db(self):
        return sqlite3.connect(self.db_path)

    def _pause_interval_for_date(self, dt_start):
        pause_start = datetime(dt_start.year, dt_start.month, dt_start.day, 13, 0, 0)
        pause_end = datetime(dt_start.year, dt_start.month, dt_start.day, 14, 0, 0)
        return pause_start, pause_end

    def _overlaps(self, start_a, end_a, start_b, end_b):
        return start_a < end_b and end_a > start_b

    def _has_conflict(self, cur, patient_id, kine_id, start_dt, end_dt, exclude_appointment_id=None):
        cur.execute(
            """
            SELECT id, patient_id, IFNULL(kine_id, 0), start_datetime, IFNULL(end_datetime, start_datetime)
            FROM appointments
            WHERE (? IS NULL OR id<>?)
            """,
            (exclude_appointment_id, exclude_appointment_id),
        )
        for row_id, row_patient, row_kine, row_start_text, row_end_text in cur.fetchall():
            row_start = datetime.strptime(row_start_text, "%Y-%m-%d %H:%M:%S")
            row_end = datetime.strptime(row_end_text, "%Y-%m-%d %H:%M:%S")
            if row_end <= row_start:
                row_end = row_start + timedelta(minutes=30)
            if not self._overlaps(start_dt, end_dt, row_start, row_end):
                continue
            if int(row_patient) == int(patient_id) or (kine_id and int(row_kine or 0) == int(kine_id)):
                return True

        pause_start, pause_end = self._pause_interval_for_date(start_dt)
        if self._overlaps(start_dt, end_dt, pause_start, pause_end):
            return True
        return False

    def _load_kines(self):
        self.fix_kine_combo.clear()
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT id, IFNULL(full_name, username) FROM users WHERE role='kine' AND active=1 ORDER BY full_name")
        rows = cur.fetchall()
        if not rows:
            cur.execute("SELECT id, IFNULL(full_name, username) FROM users WHERE role='admin' AND active=1 ORDER BY full_name")
            rows = cur.fetchall()
        conn.close()
        for kid, label in rows:
            self.fix_kine_combo.addItem(label, kid)

    def refresh_fix_tab(self):
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT * FROM (
                SELECT p.id,
                       p.nom || ' ' || IFNULL(p.prenom, '') AS patient,
                       COALESCE(pp.nature, IFNULL(m.nature_seances, '')) AS nature,
                       COALESCE(pp.prevues, IFNULL(m.nb_seances_programme, 0)) AS prevues,
                       COALESCE(pp.duree_minutes, IFNULL(m.duree_seance_minutes, 30)) AS duree_minutes,
                       IFNULL(a.programmees, 0) AS programmees,
                       COALESCE(pp.part_patient, IFNULL(f.patient_share, IFNULL(f.session_price, 0))) AS part_patient,
                       COALESCE(pp.part_cnam, IFNULL(f.cnam_share, 0)) AS part_cnam
                FROM patients p
                LEFT JOIN (
                    SELECT patient_id,
                           GROUP_CONCAT(DISTINCT IFNULL(nature_seances, '')) AS nature,
                           SUM(IFNULL(nb_seances, 0)) AS prevues,
                           MAX(IFNULL(duree_seance_minutes, 30)) AS duree_minutes,
                           MAX(IFNULL(patient_share, 0)) AS part_patient,
                           MAX(IFNULL(cnam_share, 0)) AS part_cnam
                    FROM patient_programs
                    GROUP BY patient_id
                ) pp ON pp.patient_id = p.id
                LEFT JOIN medical_records m ON m.patient_id = p.id
                LEFT JOIN (
                    SELECT patient_id, COUNT(id) AS programmees
                    FROM appointments
                    GROUP BY patient_id
                ) a ON a.patient_id = p.id
                LEFT JOIN patient_finance f ON f.patient_id = p.id
            )
            WHERE prevues > programmees
            ORDER BY patient
            """
        )
        rows = cur.fetchall()
        conn.close()

        self.fix_table.setRowCount(len(rows))
        self.fix_patient_combo.clear()
        total_remaining = 0
        for r, row in enumerate(rows):
            patient_id, patient, nature, prevues, duree_minutes, programmees, part_patient, part_cnam = row
            restantes = max(0, int(prevues or 0) - int(programmees or 0))
            total_remaining += restantes
            values = [patient, nature, int(prevues or 0), int(programmees or 0), restantes]
            for c, value in enumerate(values):
                self.fix_table.setItem(r, c, QTableWidgetItem(str(value)))
            self.fix_patient_combo.addItem(
                f"{patient} ({restantes} restantes)",
                {
                    "patient_id": patient_id,
                    "nature": nature,
                    "remaining": restantes,
                    "duree_minutes": int(duree_minutes or 30),
                    "patient_share": float(part_patient or 0),
                    "cnam_share": float(part_cnam or 0),
                },
            )

        self.fix_info_label.setText(f"Séances restantes à programmer: {total_remaining}")
        if self.fix_patient_combo.count() > 0:
            self._on_fix_patient_changed()

    def _on_fix_patient_changed(self):
        data = self.fix_patient_combo.currentData()
        if not data:
            self.fix_nb_spin.setValue(1)
            self.fix_nb_spin.setMaximum(1)
            return
        remaining = int(data.get("remaining", 1))
        self.fix_nb_spin.setMaximum(max(1, remaining))
        self.fix_nb_spin.setValue(max(1, min(remaining, self.fix_nb_spin.value())))
        self.fix_duration.setValue(int(data.get("duree_minutes", 30) or 30))

    def _sync_medical_record_from_programs(self, patient_id, cur):
        cur.execute(
            """
            SELECT IFNULL(nature_seances, ''), IFNULL(nb_seances, 0), IFNULL(duree_seance_minutes, 30),
                   IFNULL(objectifs, ''), IFNULL(remarques, '')
            FROM patient_programs
            WHERE patient_id=?
            ORDER BY id DESC
            """,
            (patient_id,),
        )
        rows = cur.fetchall()
        cur.execute("SELECT id, IFNULL(medecin_traitant, '') FROM medical_records WHERE patient_id=?", (patient_id,))
        existing = cur.fetchone()
        if rows:
            total_nb = sum(int(row[1] or 0) for row in rows)
            first = rows[0]
            if existing:
                cur.execute(
                    """
                    UPDATE medical_records
                    SET medecin_traitant=?, nb_seances_programme=?, duree_seance_minutes=?, nature_seances=?, objectifs=?, remarques=?, updated_at=CURRENT_TIMESTAMP
                    WHERE patient_id=?
                    """,
                    (
                        existing[1],
                        total_nb,
                        int(first[2] or 30),
                        first[0],
                        first[3],
                        first[4],
                        patient_id,
                    ),
                )
            else:
                cur.execute(
                    """
                    INSERT INTO medical_records(
                        patient_id, diagnostic, medecin_traitant, nb_seances_programme,
                        duree_seance_minutes, nature_seances, objectifs, remarques
                    ) VALUES (?, '', ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        patient_id,
                        '',
                        total_nb,
                        int(first[2] or 30),
                        first[0],
                        first[3],
                        first[4],
                    ),
                )
        elif existing:
            cur.execute(
                """
                UPDATE medical_records
                SET nb_seances_programme=0,
                    duree_seance_minutes=30,
                    nature_seances='',
                    objectifs='',
                    remarques='',
                    updated_at=CURRENT_TIMESTAMP
                WHERE patient_id=?
                """,
                (patient_id,),
            )

    def delete_program_from_fixation(self):
        data = self.fix_patient_combo.currentData()
        if not data:
            QMessageBox.warning(self, "Fixation RDV", "Aucun patient sélectionné.")
            return

        patient_id = int(data["patient_id"])
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT id, IFNULL(titre, ''), IFNULL(nature_seances, ''), IFNULL(nb_seances, 0), IFNULL(date_debut, ''), IFNULL(statut, 'planifie')
            FROM patient_programs
            WHERE patient_id=?
            ORDER BY id DESC
            """,
            (patient_id,),
        )
        rows = cur.fetchall()
        if not rows:
            conn.close()
            QMessageBox.information(self, "Fixation RDV", "Aucun programme à supprimer pour ce patient.")
            return

        choices = []
        labels = []
        for program_id, title, nature, nb_seances, date_debut, statut in rows:
            display = title or "(Sans titre)"
            labels.append(f"{program_id} | {display} | {nature} | {nb_seances} séances | {date_debut} | {statut}")
            choices.append(program_id)

        choice, ok = QInputDialog.getItem(
            self,
            "Supprimer un programme",
            "Choisissez le programme à supprimer:",
            labels,
            0,
            False,
        )
        if not ok:
            conn.close()
            return

        program_id = choices[labels.index(choice)]
        confirm = QMessageBox.question(
            self,
            "Confirmation",
            f"Supprimer le programme sélectionné ?\n{choice}",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm != QMessageBox.StandardButton.Yes:
            conn.close()
            return

        cur.execute("DELETE FROM patient_programs WHERE id=?", (program_id,))
        self._sync_medical_record_from_programs(patient_id, cur)
        conn.commit()
        conn.close()

        QMessageBox.information(self, "Fixation RDV", "Programme supprimé.")
        self.refresh()

    def _is_eligible_date(self, qdate, mode_name):
        dow = qdate.dayOfWeek()  # 1=Mon ... 7=Sun
        if mode_name == "Journalier":
            return dow <= 6
        if mode_name == "2 jours par semaine":
            return dow in (1, 4)
        return dow in (1, 3, 5)

    def _slot_free(self, cur, kine_id, start_dt):
        start_obj = datetime.strptime(start_dt, "%Y-%m-%d %H:%M:%S")
        end_obj = start_obj + timedelta(minutes=int(self.fix_duration.value()))
        return not self._has_conflict(cur, -1, int(kine_id), start_obj, end_obj)

    def generate_smart_appointments(self):
        data = self.fix_patient_combo.currentData()
        if not data:
            QMessageBox.warning(self, "Fixation RDV", "Aucun patient avec séances restantes.")
            return

        patient_id = int(data["patient_id"])
        nature = data.get("nature", "")
        remaining = int(data.get("remaining", 0))
        patient_share = float(data.get("patient_share", 0))
        cnam_share = float(data.get("cnam_share", 0))
        to_plan = int(self.fix_nb_spin.value())
        if remaining <= 0:
            QMessageBox.information(self, "Fixation RDV", "Aucune séance restante à programmer.")
            return
        to_plan = min(to_plan, remaining)

        kine_id = self.fix_kine_combo.currentData()
        if not kine_id:
            QMessageBox.warning(self, "Fixation RDV", "Choisissez un kiné.")
            return

        mode_name = self.fix_mode_combo.currentText()
        start_qdate = self.fix_start_date.date()
        time = self.fix_time.time()
        duration_min = int(self.fix_duration.value())

        conn = self._db()
        cur = conn.cursor()
        if nature:
            cur.execute("INSERT OR IGNORE INTO session_types(libelle) VALUES (?)", (nature,))

        planned = 0
        cursor_date = QDate(start_qdate)
        safe_guard = 0
        while planned < to_plan and safe_guard < 730:
            safe_guard += 1
            if not self._is_eligible_date(cursor_date, mode_name):
                cursor_date = cursor_date.addDays(1)
                continue

            start_dt = datetime(
                cursor_date.year(),
                cursor_date.month(),
                cursor_date.day(),
                time.hour(),
                time.minute(),
            )
            start_text = start_dt.strftime("%Y-%m-%d %H:%M:%S")
            end_text = (start_dt + timedelta(minutes=duration_min)).strftime("%Y-%m-%d %H:%M:%S")

            if not self._slot_free(cur, int(kine_id), start_text):
                cursor_date = cursor_date.addDays(1)
                continue

            if self._has_conflict(cur, patient_id, int(kine_id), start_dt, start_dt + timedelta(minutes=duration_min)):
                cursor_date = cursor_date.addDays(1)
                continue

            cur.execute(
                """
                INSERT INTO appointments(
                    patient_id, kine_id, start_datetime, end_datetime, acte,
                    room, status, payment_status, amount, paid_amount, cnam_covered
                ) VALUES (?, ?, ?, ?, ?, '', 'planifie', 'non_paye', ?, 0, ?)
                """,
                (patient_id, int(kine_id), start_text, end_text, nature, patient_share, cnam_share),
            )
            planned += 1
            cursor_date = cursor_date.addDays(1)

        conn.commit()
        conn.close()

        QMessageBox.information(self, "Fixation RDV", f"{planned} séance(s) programmée(s) automatiquement.")
        self.refresh()

    def add_appointment(self):
        dialog = AppointmentDialog(self.db_path, self.day_date.date(), self)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return

        payload = dialog.get_payload()
        if not payload["patient_id"] or not payload["kine_id"]:
            QMessageBox.warning(self, "Validation", "Patient et kiné sont obligatoires.")
            return

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            "SELECT IFNULL(session_price, 0) FROM patient_finance WHERE patient_id=?",
            (payload["patient_id"],),
        )
        row = cur.fetchone()
        default_price = float(row[0]) if row else 0.0
        amount_to_use = payload["amount"] if payload["amount"] > 0 else default_price

        start_dt = datetime.strptime(payload["start"], "%Y-%m-%d %H:%M:%S")
        end_dt = datetime.strptime(payload["end"], "%Y-%m-%d %H:%M:%S")
        if self._has_conflict(cur, int(payload["patient_id"]), int(payload["kine_id"]), start_dt, end_dt):
            conn.close()
            QMessageBox.warning(self, "Conflit", "Conflit detecte (kine/patient/duree/pause).")
            return

        cur.execute("INSERT OR IGNORE INTO session_types(libelle) VALUES (?)", (payload["acte"],))
        cur.execute(
            """
            INSERT INTO appointments(
                patient_id, kine_id, start_datetime, end_datetime, acte,
                room, status, payment_status, amount, paid_amount, cnam_covered
            ) VALUES (?, ?, ?, ?, ?, ?, 'planifie', 'non_paye', ?, 0, ?)
            """,
            (
                payload["patient_id"],
                payload["kine_id"],
                payload["start"],
                payload["end"],
                payload["acte"],
                payload.get("room", ""),
                amount_to_use,
                payload["cnam"],
            ),
        )
        conn.commit()
        conn.close()

        self.refresh()

    def save_day_statuses(self):
        conn = self._db()
        cur = conn.cursor()

        for row in range(self.day_table.rowCount()):
            appointment_id = int(self.day_table.item(row, 0).text())
            patient_name = self.day_table.item(row, 2).text() if self.day_table.item(row, 2) else ""
            status = self.day_table.item(row, 5).text().strip().lower()
            payment_status = self.day_table.item(row, 6).text().strip().lower()
            paid_text = self.day_table.item(row, 8).text().strip().replace(",", ".")

            if status not in ["planifie", "present", "absent", "effectue"]:
                status = "planifie"
            if payment_status not in ["non_paye", "partiel", "paye"]:
                payment_status = "non_paye"
            try:
                paid_amount = float(paid_text or 0)
            except ValueError:
                paid_amount = 0

            payment_status, paid_total = self._apply_payment_rules(cur, appointment_id, status, paid_amount)

            if self.day_table.item(row, 6):
                self.day_table.item(row, 6).setText(payment_status)
            if self.day_table.item(row, 8):
                self.day_table.item(row, 8).setText(f"{paid_total:.2f}")

        conn.commit()
        conn.close()

        QMessageBox.information(self, "Succès", "Mise à jour effectuée.")
        self.refresh()

    def _apply_payment_rules(self, cur, appointment_id, status, paid_amount):
        return apply_payment_with_fifo(cur, appointment_id, status, paid_amount, reason="rendezvous")

    def _project_planned_advance_allocations(self):
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT patient_id, IFNULL(advance_balance, 0) FROM patient_finance WHERE IFNULL(advance_balance, 0) > 0")
        balances = {int(pid): float(balance or 0) for pid, balance in cur.fetchall()}
        projected = {}

        for patient_id, balance in balances.items():
            cur.execute(
                """
                SELECT id, IFNULL(amount, 0), IFNULL(paid_amount, 0)
                FROM appointments
                WHERE patient_id=? AND status='planifie'
                ORDER BY start_datetime, id
                """,
                (patient_id,),
            )
            for appointment_id, amount, paid_amount in cur.fetchall():
                due = max(0.0, float(amount or 0) - float(paid_amount or 0))
                if due <= 0 or balance <= 0:
                    continue
                use = min(balance, due)
                balance -= use
                projected[int(appointment_id)] = {
                    "paid_total": float(paid_amount or 0) + use,
                    "status": "paye" if (float(paid_amount or 0) + use) >= float(amount or 0) else "partiel",
                    "used": use,
                }
        conn.close()
        return projected

    def _toggle_presence(self, appointment_id):
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT IFNULL(status, 'planifie'), IFNULL(paid_amount, 0) FROM appointments WHERE id=?", (appointment_id,))
        row = cur.fetchone()
        if not row:
            conn.close()
            return
        current_status, paid_amount = row
        new_status = "present" if current_status not in ("present", "effectue") else "absent"
        self._apply_payment_rules(cur, appointment_id, new_status, float(paid_amount or 0))
        conn.commit()
        conn.close()
        self.refresh()

    def _manage_payment(self, appointment_id):
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT IFNULL(paid_amount, 0), IFNULL(status, 'planifie') FROM appointments WHERE id=?", (appointment_id,))
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

        self._apply_payment_rules(cur, appointment_id, status, float(amount))
        conn.commit()
        conn.close()
        self.refresh()

    def load_week(self):
        qdate = self.week_date.date()
        monday = qdate.addDays(-(qdate.dayOfWeek() - 1))
        sunday = monday.addDays(6)

        self._reset_week_spans()

        for r in range(self.week_table.rowCount()):
            for c in range(1, 8):
                self.week_table.setItem(r, c, None)

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
             SELECT a.id,
                 a.start_datetime,
                   IFNULL(a.end_datetime, ''),
                   p.nom || ' ' || IFNULL(p.prenom, ''),
                   IFNULL(u.full_name, u.username),
                   IFNULL(a.acte, ''),
                   IFNULL(a.room, '')
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            LEFT JOIN users u ON u.id = a.kine_id
            WHERE date(a.start_datetime) BETWEEN ? AND ?
            ORDER BY a.start_datetime
            """,
            (monday.toString("yyyy-MM-dd"), sunday.toString("yyyy-MM-dd")),
        )

        for appointment_id, start_dt, end_dt, patient, kine, acte, room in cur.fetchall():
            dt_start = datetime.strptime(start_dt, "%Y-%m-%d %H:%M:%S")
            if end_dt:
                dt_end = datetime.strptime(end_dt, "%Y-%m-%d %H:%M:%S")
            else:
                dt_end = dt_start + timedelta(minutes=30)
            if dt_end <= dt_start:
                dt_end = dt_start + timedelta(minutes=30)

            col = dt_start.isoweekday()
            if not (1 <= col <= 7):
                continue

            txt = f"{patient}\n{kine}\n{acte}\n{room}".strip()
            for row in range(self.week_table.rowCount()):
                slot_start = datetime(dt_start.year, dt_start.month, dt_start.day, 8 + row, 0, 0)
                slot_end = slot_start + timedelta(hours=1)
                if not (dt_start < slot_end and dt_end > slot_start):
                    continue

                existing = self.week_table.item(row, col)
                if existing is None or not existing.text().strip() or existing.text().strip() == "---":
                    item = QTableWidgetItem(txt)
                    item.setData(Qt.ItemDataRole.UserRole, [int(appointment_id)])
                else:
                    current_text = existing.text().strip()
                    item = existing
                    item.setText((current_text + "\n---\n" + txt).strip())
                    ids = list(item.data(Qt.ItemDataRole.UserRole) or [])
                    if int(appointment_id) not in ids:
                        ids.append(int(appointment_id))
                    item.setData(Qt.ItemDataRole.UserRole, ids)
                item.setTextAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignTop)
                self.week_table.setItem(row, col, item)

        conn.close()
        self._merge_week_vertical_same_programs()

    def load_day(self):
        selected = self.day_date.date().toString("yyyy-MM-dd")
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT a.id,
                   strftime('%H:%M', a.start_datetime),
                   p.nom || ' ' || IFNULL(p.prenom, ''),
                   IFNULL(u.full_name, u.username),
                     CASE WHEN IFNULL(a.room, '') <> '' THEN IFNULL(a.acte, '') || ' | ' || a.room ELSE IFNULL(a.acte, '') END,
                   IFNULL(a.status, 'planifie'),
                   IFNULL(a.payment_status, 'non_paye'),
                   IFNULL(a.amount, 0),
                   IFNULL(a.paid_amount, 0),
                   IFNULL(au.amount_used, 0)
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            LEFT JOIN users u ON u.id = a.kine_id
            LEFT JOIN advance_usage au ON au.appointment_id = a.id
            WHERE date(a.start_datetime) = ?
            ORDER BY a.start_datetime
            """,
            (selected,),
        )
        rows = cur.fetchall()
        conn.close()
        projected = self._project_planned_advance_allocations()

        self.day_table.setRowCount(len(rows))
        for r, row in enumerate(rows):
            appointment_id = int(row[0])
            for c, value in enumerate(row[:9]):
                self.day_table.setItem(r, c, QTableWidgetItem(str(value)))
            used_advance = float(row[9] or 0)
            if appointment_id in projected and used_advance <= 0:
                proj = projected[appointment_id]
                self.day_table.item(r, 6).setText(f"{proj['status']} (avance prévue)")
                self.day_table.item(r, 8).setText(f"{proj['paid_total']:.2f}")
            if used_advance > 0:
                current_text = self.day_table.item(r, 6).text()
                self.day_table.item(r, 6).setText(f"{current_text} (avance)")

            btn_presence = QPushButton("Présence")
            btn_presence.clicked.connect(lambda _, aid=appointment_id: self._toggle_presence(aid))
            self.day_table.setCellWidget(r, 9, btn_presence)

            btn_payment = QPushButton("Paiement")
            btn_payment.clicked.connect(lambda _, aid=appointment_id: self._manage_payment(aid))
            self.day_table.setCellWidget(r, 10, btn_payment)

    def _open_appointment_sheet(self, appointment_id):
        dialog = AppointmentSheetDialog(self.db_path, appointment_id, self)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            self.refresh()

    def _on_day_item_double_clicked(self, item):
        row = item.row()
        id_item = self.day_table.item(row, 0)
        if not id_item:
            return
        self._open_appointment_sheet(int(id_item.text()))

    def _on_week_item_double_clicked(self, item):
        if item.column() == 0:
            return
        ids = item.data(Qt.ItemDataRole.UserRole) or []
        ids = [int(v) for v in ids if v is not None]
        if not ids:
            return
        if len(ids) == 1:
            self._open_appointment_sheet(ids[0])
            return

        conn = self._db()
        cur = conn.cursor()
        options = []
        labels = []
        for appointment_id in ids:
            cur.execute(
                """
                SELECT strftime('%H:%M', a.start_datetime),
                       p.nom || ' ' || IFNULL(p.prenom, ''),
                       IFNULL(u.full_name, u.username)
                FROM appointments a
                JOIN patients p ON p.id = a.patient_id
                LEFT JOIN users u ON u.id = a.kine_id
                WHERE a.id=?
                """,
                (appointment_id,),
            )
            row = cur.fetchone()
            if not row:
                continue
            label = f"{appointment_id} | {row[0]} | {row[1]} | {row[2]}"
            labels.append(label)
            options.append(appointment_id)
        conn.close()
        if not labels:
            return

        choice, ok = QInputDialog.getItem(
            self,
            "Choisir la seance",
            "Plusieurs seances sont dans ce bloc. Choisissez une seance:",
            labels,
            0,
            False,
        )
        if not ok:
            return
        idx = labels.index(choice)
        self._open_appointment_sheet(options[idx])

    def _on_week_cell_clicked(self, row, col):
        if not self.week_move_mode_btn.isChecked() or col == 0:
            return
        item = self.week_table.item(row, col)
        if item is None:
            return
        ids = [int(v) for v in (item.data(Qt.ItemDataRole.UserRole) or []) if v is not None]
        if not ids:
            return
        appointment_id = ids[0]

        target_day = self.week_date.date().addDays(-(self.week_date.date().dayOfWeek() - 1)).addDays(col - 1)
        target_start = datetime(target_day.year(), target_day.month(), target_day.day(), 8 + row, 0, 0)

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            "SELECT patient_id, IFNULL(kine_id, 0), start_datetime, IFNULL(end_datetime, start_datetime) FROM appointments WHERE id=?",
            (appointment_id,),
        )
        appt = cur.fetchone()
        if not appt:
            conn.close()
            return
        patient_id, kine_id, old_start_text, old_end_text = appt
        old_start = datetime.strptime(old_start_text, "%Y-%m-%d %H:%M:%S")
        old_end = datetime.strptime(old_end_text, "%Y-%m-%d %H:%M:%S")
        if old_end <= old_start:
            old_end = old_start + timedelta(minutes=30)
        duration = old_end - old_start
        target_end = target_start + duration

        if self._has_conflict(cur, int(patient_id), int(kine_id or 0), target_start, target_end, exclude_appointment_id=appointment_id):
            conn.close()
            QMessageBox.warning(self, "Conflit", "Deplacement impossible (conflit kine/patient/pause).")
            return

        cur.execute(
            "UPDATE appointments SET start_datetime=?, end_datetime=? WHERE id=?",
            (
                target_start.strftime("%Y-%m-%d %H:%M:%S"),
                target_end.strftime("%Y-%m-%d %H:%M:%S"),
                appointment_id,
            ),
        )
        conn.commit()
        conn.close()
        self.refresh()

    def load_month_day_details(self):
        selected = self.calendar.selectedDate().toString("yyyy-MM-dd")
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT strftime('%H:%M', a.start_datetime),
                   p.nom || ' ' || IFNULL(p.prenom, ''),
                   IFNULL(a.acte, ''),
                   IFNULL(u.full_name, u.username)
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            LEFT JOIN users u ON u.id = a.kine_id
            WHERE date(a.start_datetime) = ?
            ORDER BY a.start_datetime
            """,
            (selected,),
        )
        rows = cur.fetchall()
        conn.close()

        self.month_list.clear()
        if not rows:
            self.month_list.addItem("Aucune séance programmée pour cette date.")
            return

        for heure, patient, acte, kine in rows:
            self.month_list.addItem(f"{heure} - {patient} - {acte} ({kine})")

    def refresh(self):
        self._load_kines()
        self.refresh_fix_tab()
        self.load_week()
        self.load_day()
        self.load_month_day_details()
        self.refresh_load_tab()

    def refresh_load_tab(self):
        qdate = self.week_date.date()
        monday = qdate.addDays(-(qdate.dayOfWeek() - 1))
        sunday = monday.addDays(6)

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT IFNULL(u.full_name, u.username),
                   COUNT(a.id),
                   IFNULL(SUM(
                        CASE
                            WHEN IFNULL(a.end_datetime, '') = '' THEN 30
                            ELSE CAST((julianday(a.end_datetime) - julianday(a.start_datetime)) * 24 * 60 AS INTEGER)
                        END
                   ), 0)
            FROM users u
            LEFT JOIN appointments a ON a.kine_id=u.id AND date(a.start_datetime) BETWEEN ? AND ?
            WHERE u.role IN ('kine', 'admin') AND IFNULL(u.active, 1)=1
            GROUP BY u.id
            ORDER BY 3 DESC
            """,
            (monday.toString("yyyy-MM-dd"), sunday.toString("yyyy-MM-dd")),
        )
        rows = cur.fetchall()
        conn.close()

        self.load_table.setRowCount(len(rows))
        for r, row in enumerate(rows):
            self.load_table.setItem(r, 0, QTableWidgetItem(str(row[0] or "")))
            self.load_table.setItem(r, 1, QTableWidgetItem(str(int(row[1] or 0))))
            self.load_table.setItem(r, 2, QTableWidgetItem(str(int(row[2] or 0))))

    def balance_weekly_load(self):
        qdate = self.week_date.date()
        monday = qdate.addDays(-(qdate.dayOfWeek() - 1))
        sunday = monday.addDays(6)

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT u.id, IFNULL(SUM(
                        CASE
                            WHEN IFNULL(a.end_datetime, '') = '' THEN 30
                            ELSE CAST((julianday(a.end_datetime) - julianday(a.start_datetime)) * 24 * 60 AS INTEGER)
                        END
                   ), 0) AS total_min
            FROM users u
            LEFT JOIN appointments a ON a.kine_id=u.id AND date(a.start_datetime) BETWEEN ? AND ?
            WHERE u.role IN ('kine', 'admin') AND IFNULL(u.active, 1)=1
            GROUP BY u.id
            ORDER BY total_min DESC
            """,
            (monday.toString("yyyy-MM-dd"), sunday.toString("yyyy-MM-dd")),
        )
        loads = cur.fetchall()
        if len(loads) < 2:
            conn.close()
            return

        busiest_id = int(loads[0][0])
        least_id = int(loads[-1][0])
        moved = 0

        cur.execute(
            """
            SELECT id, patient_id, start_datetime, IFNULL(end_datetime, start_datetime)
            FROM appointments
            WHERE kine_id=? AND date(start_datetime) BETWEEN ? AND ? AND status='planifie'
            ORDER BY start_datetime DESC
            """,
            (busiest_id, monday.toString("yyyy-MM-dd"), sunday.toString("yyyy-MM-dd")),
        )
        for appointment_id, patient_id, start_text, end_text in cur.fetchall():
            start_dt = datetime.strptime(start_text, "%Y-%m-%d %H:%M:%S")
            end_dt = datetime.strptime(end_text, "%Y-%m-%d %H:%M:%S")
            if end_dt <= start_dt:
                end_dt = start_dt + timedelta(minutes=30)
            if self._has_conflict(cur, int(patient_id), least_id, start_dt, end_dt, exclude_appointment_id=int(appointment_id)):
                continue
            cur.execute("UPDATE appointments SET kine_id=? WHERE id=?", (least_id, int(appointment_id)))
            moved += 1
            if moved >= 3:
                break

        conn.commit()
        conn.close()
        QMessageBox.information(self, "Equilibrage", f"{moved} seance(s) reattribuee(s).")
        self.refresh()

    def export_day_pdf(self):
        selected = self.day_date.date().toString("yyyy-MM-dd")
        path, _ = QFileDialog.getSaveFileName(
            self,
            "Exporter planning journalier",
            f"planning_{selected}.pdf",
            "PDF Files (*.pdf)",
        )
        if not path:
            return

        headers = ["Heure", "Patient", "Kiné", "Acte", "Présence", "Paiement", "Montant", "Payé"]
        rows = []
        for row in range(self.day_table.rowCount()):
            rows.append([
                self.day_table.item(row, 1).text() if self.day_table.item(row, 1) else "",
                self.day_table.item(row, 2).text() if self.day_table.item(row, 2) else "",
                self.day_table.item(row, 3).text() if self.day_table.item(row, 3) else "",
                self.day_table.item(row, 4).text() if self.day_table.item(row, 4) else "",
                self.day_table.item(row, 5).text() if self.day_table.item(row, 5) else "",
                self.day_table.item(row, 6).text() if self.day_table.item(row, 6) else "",
                self.day_table.item(row, 7).text() if self.day_table.item(row, 7) else "",
                self.day_table.item(row, 8).text() if self.day_table.item(row, 8) else "",
            ])

        try:
            export_simple_table_pdf(path, f"Planning journalier {selected}", headers, rows)
            QMessageBox.information(self, "Export", "PDF généré avec succès.")
        except Exception as exc:
            QMessageBox.critical(self, "Export", str(exc))
