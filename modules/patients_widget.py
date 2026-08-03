import sqlite3
import re
from PyQt6.QtCore import QDate
from PyQt6.QtWidgets import (
    QWidget, QDialog, QVBoxLayout, QHBoxLayout, QFormLayout, QLabel, QLineEdit,
    QPushButton, QComboBox, QSpinBox, QTextEdit, QTableWidget,
    QTableWidgetItem, QMessageBox, QGroupBox, QTabWidget, QDoubleSpinBox,
    QDateEdit, QScrollArea, QHeaderView, QAbstractItemView
)

from modules.appointment_sheet_dialog import AppointmentSheetDialog
from modules.payment_projection import project_patient_payment_states
from modules.patient_longitudinal_widget import PatientLongitudinalWidget
from modules.finance_engine import register_advance_credit


class PatientDetailDialog(QDialog):
    def __init__(self, db_path, patient_id, parent=None):
        super().__init__(parent)
        self.db_path = db_path
        self.patient_id = int(patient_id)
        self.setWindowTitle("Fiche patient")
        self.setMinimumWidth(820)
        self._build_ui()
        self._load_patient()

    def _db(self):
        return sqlite3.connect(self.db_path)

    def _build_ui(self):
        root = QVBoxLayout(self)
        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        self.general_tab = QWidget()
        general_layout = QFormLayout(self.general_tab)
        self.detail_code_input = QLineEdit()
        self.detail_dossier_input = QLineEdit()
        self.detail_nom_input = QLineEdit()
        self.detail_prenom_input = QLineEdit()
        self.detail_birth_input = QDateEdit()
        self.detail_birth_input.setCalendarPopup(True)
        self.detail_birth_input.setDisplayFormat("dd/MM/yyyy")
        self.detail_sexe_input = QComboBox()
        self.detail_sexe_input.addItems(["Homme", "Femme", "Autre"])
        self.detail_tel1_input = QLineEdit()
        self.detail_tel2_input = QLineEdit()
        self.detail_adresse_input = QLineEdit()
        self.detail_couverture_input = QComboBox()
        self.detail_couverture_input.addItems(["CNAM", "Civil payant", "Autre prise en charge"])
        self.detail_medecin_input = QLineEdit()

        general_layout.addRow("Code patient", self.detail_code_input)
        general_layout.addRow("Numero dossier", self.detail_dossier_input)
        general_layout.addRow("Nom", self.detail_nom_input)
        general_layout.addRow("Prenom", self.detail_prenom_input)
        general_layout.addRow("Date naissance", self.detail_birth_input)
        general_layout.addRow("Sexe", self.detail_sexe_input)
        general_layout.addRow("Telephone 1", self.detail_tel1_input)
        general_layout.addRow("Telephone 2", self.detail_tel2_input)
        general_layout.addRow("Adresse", self.detail_adresse_input)
        general_layout.addRow("Couverture", self.detail_couverture_input)
        general_layout.addRow("Medecin traitant", self.detail_medecin_input)

        self.tabs.addTab(self.general_tab, "Données générales")

        self.programs_tab = QWidget()
        programs_layout = QVBoxLayout(self.programs_tab)
        self.detail_programs_table = QTableWidget(0, 6)
        self.detail_programs_table.setHorizontalHeaderLabels([
            "ID", "Titre", "Nature", "Nb seances", "Date debut", "Statut"
        ])
        self.detail_programs_table.setColumnHidden(0, True)
        self._decorate_table(self.detail_programs_table)
        programs_layout.addWidget(self.detail_programs_table)
        self.tabs.addTab(self.programs_tab, "Programmes")

        self.appointments_tab = QWidget()
        appts_layout = QVBoxLayout(self.appointments_tab)
        self.detail_appointments_table = QTableWidget(0, 7)
        self.detail_appointments_table.setHorizontalHeaderLabels([
            "ID", "Date", "Heure", "Kiné", "Acte", "Statut", "Paiement"
        ])
        self.detail_appointments_table.setColumnHidden(0, True)
        self._decorate_table(self.detail_appointments_table)
        self.detail_appointments_table.itemDoubleClicked.connect(self._on_detail_appointment_double_clicked)
        appts_layout.addWidget(self.detail_appointments_table)
        self.tabs.addTab(self.appointments_tab, "Rendez-vous")

        buttons = QHBoxLayout()
        save_btn = QPushButton("Enregistrer")
        save_btn.clicked.connect(self._save_patient)
        close_btn = QPushButton("Fermer")
        close_btn.clicked.connect(self.accept)
        buttons.addStretch()
        buttons.addWidget(save_btn)
        buttons.addWidget(close_btn)
        root.addLayout(buttons)

    def _decorate_table(self, table):
        table.setAlternatingRowColors(True)
        table.verticalHeader().setVisible(False)
        table.setShowGrid(False)
        table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        table.horizontalHeader().setStretchLastSection(True)

    def _load_patient(self):
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT IFNULL(code_patient, ''), IFNULL(dossier_patient, ''), nom, IFNULL(prenom, ''),
                   IFNULL(date_naissance, ''), IFNULL(sexe, ''), IFNULL(telephone1, ''), IFNULL(telephone2, ''),
                   IFNULL(adresse, ''), IFNULL(couverture, ''), IFNULL(m.medecin_traitant, '')
            FROM patients p
            LEFT JOIN medical_records m ON m.patient_id = p.id
            WHERE p.id=?
            """,
            (self.patient_id,),
        )
        row = cur.fetchone()
        if not row:
            conn.close()
            return
        (code, dossier, nom, prenom, birth_text, sexe, tel1, tel2, adresse, couverture, medecin) = row
        self.detail_code_input.setText(code)
        self.detail_dossier_input.setText(dossier)
        self.detail_nom_input.setText(nom)
        self.detail_prenom_input.setText(prenom)
        birth_qdate = QDate.fromString(birth_text, "yyyy-MM-dd") if birth_text else QDate()
        if not birth_qdate.isValid():
            birth_qdate = QDate.currentDate().addYears(-30)
        self.detail_birth_input.setDate(birth_qdate)
        self.detail_sexe_input.setCurrentText(sexe)
        self.detail_tel1_input.setText(tel1)
        self.detail_tel2_input.setText(tel2)
        self.detail_adresse_input.setText(adresse)
        self.detail_couverture_input.setCurrentText(couverture)
        self.detail_medecin_input.setText(medecin)

        self._load_patient_programs(cur)
        self._load_patient_appointments(cur)
        conn.close()

    def _load_patient_programs(self, cur):
        cur.execute(
            """
            SELECT id, IFNULL(titre, ''), IFNULL(nature_seances, ''), IFNULL(nb_seances, 0),
                   IFNULL(date_debut, ''), IFNULL(statut, 'planifie')
            FROM patient_programs
            WHERE patient_id=?
            ORDER BY id DESC
            """,
            (self.patient_id,),
        )
        rows = cur.fetchall()
        self.detail_programs_table.setRowCount(len(rows))
        for r, row in enumerate(rows):
            for c, value in enumerate(row):
                self.detail_programs_table.setItem(r, c, QTableWidgetItem(str(value)))

    def _load_patient_appointments(self, cur):
        cur.execute(
            """
            SELECT a.id,
                   strftime('%Y-%m-%d', a.start_datetime),
                   strftime('%H:%M', a.start_datetime),
                   IFNULL(u.full_name, u.username),
                   IFNULL(a.acte, ''),
                   IFNULL(a.status, ''),
                   IFNULL(a.payment_status, '')
            FROM appointments a
            LEFT JOIN users u ON u.id = a.kine_id
            WHERE a.patient_id=?
            ORDER BY a.start_datetime
            """,
            (self.patient_id,),
        )
        rows = cur.fetchall()
        self.detail_appointments_table.setRowCount(len(rows))
        for r, row in enumerate(rows):
            for c, value in enumerate(row):
                self.detail_appointments_table.setItem(r, c, QTableWidgetItem(str(value)))

    def _save_patient(self):
        conn = self._db()
        cur = conn.cursor()
        birth_date = self.detail_birth_input.date()
        date_naissance_value = birth_date.toString("yyyy-MM-dd")
        cur.execute(
            """
            UPDATE patients
            SET code_patient=?, dossier_patient=?, nom=?, prenom=?, date_naissance=?, sexe=?, telephone1=?, telephone2=?, adresse=?, couverture=?
            WHERE id=?
            """,
            (
                self.detail_code_input.text().strip() or None,
                self.detail_dossier_input.text().strip() or None,
                self.detail_nom_input.text().strip(),
                self.detail_prenom_input.text().strip(),
                date_naissance_value,
                self.detail_sexe_input.currentText(),
                self.detail_tel1_input.text().strip(),
                self.detail_tel2_input.text().strip(),
                self.detail_adresse_input.text().strip(),
                self.detail_couverture_input.currentText(),
                self.patient_id,
            ),
        )
        cur.execute("SELECT id FROM medical_records WHERE patient_id=?", (self.patient_id,))
        med = cur.fetchone()
        if med:
            cur.execute(
                """
                UPDATE medical_records
                SET medecin_traitant=?, updated_at=CURRENT_TIMESTAMP
                WHERE patient_id=?
                """,
                (
                    self.detail_medecin_input.text().strip(),
                    self.patient_id,
                ),
            )
        else:
            cur.execute(
                """
                INSERT INTO medical_records(patient_id, diagnostic, medecin_traitant, nb_seances_programme, duree_seance_minutes, nature_seances, objectifs, remarques)
                VALUES (?, '', ?, 0, 30, '', '', '')
                """,
                (
                    self.patient_id,
                    self.detail_medecin_input.text().strip(),
                ),
            )
        conn.commit()
        conn.close()
        QMessageBox.information(self, "Succes", "Fiche patient mise à jour.")
        self.accept()

    def _on_detail_appointment_double_clicked(self, item):
        row = item.row()
        appt_id_item = self.detail_appointments_table.item(row, 0)
        if not appt_id_item:
            return
        appointment_id = int(appt_id_item.text())
        dialog = AppointmentSheetDialog(self.db_path, appointment_id, self)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            self._load_patient()


class PatientsWidget(QWidget):
    def __init__(self, db_path):
        super().__init__()
        self.db_path = db_path
        self.selected_patient_id = None
        self.editing_program_id = None
        self._build_ui()
        self.refresh()

    def _build_ui(self):
        root = QVBoxLayout(self)

        title = QLabel("Gestion des patients")
        title.setObjectName("sectionTitle")
        root.addWidget(title)

        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        self.create_tab = QWidget()
        self.edit_tab = QWidget()
        self.program_tab = QWidget()
        self.payment_tab = QWidget()
        self.longitudinal_tab = PatientLongitudinalWidget(db_path=self.db_path)
        self.tabs.addTab(self.create_tab, "Creation patient")
        self.tabs.addTab(self.edit_tab, "Modification")
        self.tabs.addTab(self.program_tab, "Programme séances")
        self.tabs.addTab(self.payment_tab, "Etat des paiements et avances")
        self.tabs.addTab(self.longitudinal_tab, "Dossier longitudinal")

        self._build_create_tab()
        self._build_edit_tab()
        self._build_program_tab()
        self._build_payment_tab()
        self._load_natures()

    def apply_permissions(self, permissions):
        permissions = permissions or {}
        mapping = {
            "patients.create": 0,
            "patients.edit": 1,
            "patients.programs": 2,
            "patients.payment": 3,
            "patients.longitudinal": 4,
        }
        for key, tab_index in mapping.items():
            if key in permissions:
                self.tabs.setTabVisible(tab_index, bool(permissions[key]))

    def _decorate_table(self, table):
        table.setAlternatingRowColors(True)
        table.verticalHeader().setVisible(False)
        table.setShowGrid(False)
        table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        table.horizontalHeader().setStretchLastSection(True)

    def _db(self):
        return sqlite3.connect(self.db_path)

    def _compute_next_patient_code(self, cur):
        cur.execute("SELECT IFNULL(code_patient, '') FROM patients")
        max_index = 0
        for (code_value,) in cur.fetchall():
            code_text = str(code_value or "").strip()
            match = re.match(r"^[Pp](\d+)$", code_text)
            if not match:
                continue
            max_index = max(max_index, int(match.group(1)))
        return f"P{max_index + 1}"

    def _refresh_next_patient_code(self):
        conn = self._db()
        cur = conn.cursor()
        next_code = self._compute_next_patient_code(cur)
        conn.close()
        self.code_input.setText(next_code)

    def _load_natures(self):
        self.nature_input.clear()
        self.edit_nature_input.clear()
        if hasattr(self, "program_nature_edit"):
            self.program_nature_edit.clear()
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT libelle FROM session_types ORDER BY libelle")
        for (label,) in cur.fetchall():
            self.nature_input.addItem(label)
            self.edit_nature_input.addItem(label)
            if hasattr(self, "program_nature_edit"):
                self.program_nature_edit.addItem(label)
        conn.close()

    def _compute_age_from_qdate(self, birth_qdate):
        today = QDate.currentDate()
        age = today.year() - birth_qdate.year()
        if (today.month(), today.day()) < (birth_qdate.month(), birth_qdate.day()):
            age -= 1
        return max(0, age)

    def _update_create_age_label(self):
        age = self._compute_age_from_qdate(self.birth_input.date())
        self.age_calc_label.setText(f"{age} ans")

    def _update_edit_age_label(self):
        age = self._compute_age_from_qdate(self.edit_birth_input.date())
        self.edit_age_calc_label.setText(f"{age} ans")

    def _build_create_tab(self):
        layout = QVBoxLayout(self.create_tab)
        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        container = QWidget()
        content = QVBoxLayout(container)

        general_group = QGroupBox("Details generaux")
        general_form = QFormLayout(general_group)

        self.code_input = QLineEdit()
        self.code_input.setReadOnly(True)
        self.dossier_input = QLineEdit()
        self.nom_input = QLineEdit()
        self.prenom_input = QLineEdit()
        self.birth_input = QDateEdit()
        self.birth_input.setCalendarPopup(True)
        self.birth_input.setDisplayFormat("dd/MM/yyyy")
        self.birth_input.setDate(QDate.currentDate().addYears(-30))
        self.birth_input.dateChanged.connect(self._update_create_age_label)
        self.age_calc_label = QLabel("30 ans")
        self.sexe_input = QComboBox()
        self.sexe_input.addItems(["Homme", "Femme", "Autre"])
        self.tel1_input = QLineEdit()
        self.tel2_input = QLineEdit()
        self.adresse_input = QLineEdit()
        self.couverture_input = QComboBox()
        self.couverture_input.addItems(["CNAM", "Civil payant", "Autre prise en charge"])

        general_form.addRow("Code patient", self.code_input)
        general_form.addRow("Numero dossier", self.dossier_input)
        general_form.addRow("Nom", self.nom_input)
        general_form.addRow("Prenom", self.prenom_input)
        general_form.addRow("Date naissance", self.birth_input)
        general_form.addRow("Age calcule", self.age_calc_label)
        general_form.addRow("Sexe", self.sexe_input)
        general_form.addRow("Telephone 1", self.tel1_input)
        general_form.addRow("Telephone 2", self.tel2_input)
        general_form.addRow("Adresse", self.adresse_input)
        general_form.addRow("Couverture", self.couverture_input)

        content.addWidget(general_group)

        bottom = QHBoxLayout()
        save_btn = QPushButton("Enregistrer")
        save_btn.clicked.connect(self.save_patient)
        clear_btn = QPushButton("Nouveau")
        clear_btn.clicked.connect(self.clear_form)
        bottom.addWidget(save_btn)
        bottom.addWidget(clear_btn)
        bottom.addStretch()
        content.addLayout(bottom)
        content.addStretch()
        scroll_area.setWidget(container)
        layout.addWidget(scroll_area)

    def _build_edit_tab(self):
        layout = QVBoxLayout(self.edit_tab)

        selector_row = QHBoxLayout()
        selector_row.addWidget(QLabel("Recherche patient"))
        self.patient_search_input = QLineEdit()
        self.patient_search_input.setPlaceholderText("Tapez nom, prenom, code ou telephone")
        self.patient_search_input.textChanged.connect(self._on_patient_search_changed)
        selector_row.addWidget(self.patient_search_input, 2)

        selector_row.addWidget(QLabel("Liste filtree"))
        self.patient_combo = QComboBox()
        self.patient_combo.currentIndexChanged.connect(self._on_patient_combo_changed)
        selector_row.addWidget(self.patient_combo, 2)
        layout.addLayout(selector_row)

        self.table = QTableWidget(0, 13)
        self.table.setHorizontalHeaderLabels([
            "ID", "Code", "Numero dossier", "Nom", "Prenom", "Age", "Sexe", "Telephone", "Couverture",
            "Medecin", "Total paye", "Avance dispo", "Date naissance"
        ])
        self.table.setColumnHidden(0, True)
        self._decorate_table(self.table)
        self.table.itemSelectionChanged.connect(self._fill_from_selection)
        self.table.itemDoubleClicked.connect(self._open_patient_detail_dialog)
        layout.addWidget(self.table)

        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        scroll_container = QWidget()
        container_layout = QVBoxLayout(scroll_container)

        edit_group = QGroupBox("Modifier le patient selectionne")
        edit_form = QFormLayout(edit_group)

        self.edit_code_input = QLineEdit()
        self.edit_dossier_input = QLineEdit()
        self.edit_nom_input = QLineEdit()
        self.edit_prenom_input = QLineEdit()
        self.edit_birth_input = QDateEdit()
        self.edit_birth_input.setCalendarPopup(True)
        self.edit_birth_input.setDisplayFormat("dd/MM/yyyy")
        self.edit_birth_input.setDate(QDate.currentDate().addYears(-30))
        self.edit_birth_input.dateChanged.connect(self._update_edit_age_label)
        self.edit_age_calc_label = QLabel("30 ans")
        self.edit_sexe_input = QComboBox()
        self.edit_sexe_input.addItems(["Homme", "Femme", "Autre"])
        self.edit_tel1_input = QLineEdit()
        self.edit_tel2_input = QLineEdit()
        self.edit_adresse_input = QLineEdit()
        self.edit_couverture_input = QComboBox()
        self.edit_couverture_input.addItems(["CNAM", "Civil payant", "Autre prise en charge"])
        self.edit_medecin_input = QLineEdit()

        edit_form.addRow("Code patient", self.edit_code_input)
        edit_form.addRow("Numero dossier", self.edit_dossier_input)
        edit_form.addRow("Nom", self.edit_nom_input)
        edit_form.addRow("Prenom", self.edit_prenom_input)
        edit_form.addRow("Date naissance", self.edit_birth_input)
        edit_form.addRow("Age calcule", self.edit_age_calc_label)
        edit_form.addRow("Sexe", self.edit_sexe_input)
        edit_form.addRow("Telephone 1", self.edit_tel1_input)
        edit_form.addRow("Telephone 2", self.edit_tel2_input)
        edit_form.addRow("Adresse", self.edit_adresse_input)
        edit_form.addRow("Couverture", self.edit_couverture_input)
        edit_form.addRow("Medecin traitant", self.edit_medecin_input)

        save_edit_btn = QPushButton("Enregistrer les modifications")
        save_edit_btn.clicked.connect(self.update_selected_patient)
        edit_form.addRow(save_edit_btn)

        container_layout.addWidget(edit_group)
        scroll_area.setWidget(scroll_container)
        layout.addWidget(scroll_area)

    def _build_program_tab(self):
        layout = QVBoxLayout(self.program_tab)

        selector_row = QHBoxLayout()
        selector_row.addWidget(QLabel("Recherche patient"))
        self.program_search_input = QLineEdit()
        self.program_search_input.setPlaceholderText("Tapez nom, prenom, code ou telephone")
        self.program_search_input.textChanged.connect(self._on_program_search_changed)
        selector_row.addWidget(self.program_search_input, 2)

        selector_row.addWidget(QLabel("Patient"))
        self.program_patient_combo = QComboBox()
        self.program_patient_combo.currentIndexChanged.connect(self._on_program_patient_combo_changed)
        selector_row.addWidget(self.program_patient_combo, 2)
        layout.addLayout(selector_row)

        self.programs_table = QTableWidget(0, 12)
        self.programs_table.setHorizontalHeaderLabels([
            "ID", "Titre", "Nature", "Nb seances", "Duree", "Date debut", "Statut", "Prix seance", "Part patient", "Part CNAM", "Objectifs", "Remarques"
        ])
        self.programs_table.setColumnHidden(0, True)
        self._decorate_table(self.programs_table)
        self.programs_table.itemSelectionChanged.connect(self._on_program_row_selected)
        layout.addWidget(self.programs_table)

        form = QFormLayout()
        self.program_title_edit = QLineEdit()
        self.program_nature_edit = QComboBox()
        self.program_nature_edit.setEditable(True)
        self.program_nb_edit = QSpinBox()
        self.program_nb_edit.setRange(0, 500)
        self.program_duration_edit = QSpinBox()
        self.program_duration_edit.setRange(10, 240)
        self.program_duration_edit.setSingleStep(5)
        self.program_duration_edit.setValue(30)
        self.program_start_edit = QDateEdit()
        self.program_start_edit.setCalendarPopup(True)
        self.program_start_edit.setDisplayFormat("dd/MM/yyyy")
        self.program_start_edit.setDate(QDate.currentDate())
        self.program_status_combo = QComboBox()
        self.program_status_combo.addItems(["planifie", "en cours", "termine", "annule"])
        self.program_price_edit = QDoubleSpinBox()
        self.program_price_edit.setRange(0, 10000)
        self.program_price_edit.setDecimals(2)
        self.program_price_edit.setValue(30)
        self.program_patient_share_edit = QDoubleSpinBox()
        self.program_patient_share_edit.setRange(0, 10000)
        self.program_patient_share_edit.setDecimals(2)
        self.program_patient_share_edit.setValue(0)
        self.program_cnam_share_edit = QDoubleSpinBox()
        self.program_cnam_share_edit.setRange(0, 10000)
        self.program_cnam_share_edit.setDecimals(2)
        self.program_cnam_share_edit.setValue(0)
        self.program_objectifs_edit = QTextEdit()
        self.program_objectifs_edit.setMaximumHeight(60)
        self.program_remarques_edit = QTextEdit()
        self.program_remarques_edit.setMaximumHeight(60)

        form.addRow("Titre programme", self.program_title_edit)
        form.addRow("Nature des seances", self.program_nature_edit)
        form.addRow("Nb seances", self.program_nb_edit)
        form.addRow("Duree (min)", self.program_duration_edit)
        form.addRow("Date debut", self.program_start_edit)
        form.addRow("Statut", self.program_status_combo)
        form.addRow("Prix seance", self.program_price_edit)
        form.addRow("Part patient", self.program_patient_share_edit)
        form.addRow("Part CNAM", self.program_cnam_share_edit)
        form.addRow("Objectifs", self.program_objectifs_edit)
        form.addRow("Remarques", self.program_remarques_edit)
        layout.addLayout(form)

        buttons = QHBoxLayout()
        save_program_btn = QPushButton("Enregistrer programme")
        save_program_btn.clicked.connect(self.save_program_for_patient)
        reset_program_btn = QPushButton("Réinitialiser")
        reset_program_btn.clicked.connect(self._clear_program_form)
        delete_program_btn = QPushButton("Supprimer programme")
        delete_program_btn.clicked.connect(self.delete_selected_program)
        buttons.addWidget(save_program_btn)
        buttons.addWidget(reset_program_btn)
        buttons.addWidget(delete_program_btn)
        buttons.addStretch()
        layout.addLayout(buttons)

    def _load_natures(self):
        self.edit_nature_input.clear()
        if hasattr(self, "program_nature_edit"):
            self.program_nature_edit.clear()
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT libelle FROM session_types ORDER BY libelle")
        for (label,) in cur.fetchall():
            self.edit_nature_input.addItem(label)
            if hasattr(self, "program_nature_edit"):
                self.program_nature_edit.addItem(label)
        conn.close()

    def _build_payment_tab(self):
        layout = QVBoxLayout(self.payment_tab)

        selector_row = QHBoxLayout()
        selector_row.addWidget(QLabel("Recherche patient"))
        self.payment_search_input = QLineEdit()
        self.payment_search_input.setPlaceholderText("Tapez nom, prenom, code ou telephone")
        self.payment_search_input.textChanged.connect(self._on_payment_search_changed)
        selector_row.addWidget(self.payment_search_input, 2)

        selector_row.addWidget(QLabel("Liste filtree"))
        self.payment_patient_combo = QComboBox()
        self.payment_patient_combo.currentIndexChanged.connect(self._on_payment_combo_changed)
        selector_row.addWidget(self.payment_patient_combo, 2)
        layout.addLayout(selector_row)

        top = QHBoxLayout()
        self.payment_selected_label = QLabel("Patient selectionne: -")
        top.addWidget(self.payment_selected_label)
        refresh_btn = QPushButton("Actualiser etat")
        refresh_btn.clicked.connect(self.refresh_payment_views)
        top.addWidget(refresh_btn)
        top.addStretch()
        layout.addLayout(top)

        self.payment_subtabs = QTabWidget()
        layout.addWidget(self.payment_subtabs)

        advances_tab = QWidget()
        advances_layout = QVBoxLayout(advances_tab)

        finance_group = QGroupBox("Avances")
        finance_form = QFormLayout(finance_group)
        self.total_paid_label = QLabel("0.00")
        self.total_advance_label = QLabel("0.00")
        self.advance_balance_label = QLabel("0.00")
        self.advance_add_input = QDoubleSpinBox()
        self.advance_add_input.setRange(0, 100000)
        self.advance_add_input.setDecimals(2)

        finance_form.addRow("Total paye seances", self.total_paid_label)
        finance_form.addRow("Total avances versees", self.total_advance_label)
        finance_form.addRow("Solde avance disponible", self.advance_balance_label)
        finance_form.addRow("Nouvelle avance", self.advance_add_input)

        add_advance_btn = QPushButton("Ajouter avance")
        add_advance_btn.clicked.connect(self.add_advance_for_selected)
        finance_form.addRow(add_advance_btn)

        advances_layout.addWidget(finance_group)

        self.advances_table = QTableWidget(0, 3)
        self.advances_table.setHorizontalHeaderLabels(["Date", "Montant", "Note"])
        self._decorate_table(self.advances_table)
        advances_layout.addWidget(self.advances_table)

        payment_state_tab = QWidget()
        payment_state_layout = QVBoxLayout(payment_state_tab)
        self.payment_state_table = QTableWidget(0, 7)
        self.payment_state_table.setHorizontalHeaderLabels([
            "Date", "Acte", "Presence", "Paiement", "Montant", "Paye", "Avance utilisee"
        ])
        self._decorate_table(self.payment_state_table)
        payment_state_layout.addWidget(self.payment_state_table)

        self.payment_subtabs.addTab(advances_tab, "Avances")
        self.payment_subtabs.addTab(payment_state_tab, "Etat des paiements")

    def _fetch_patient_selector_rows(self, search_text=""):
        conn = self._db()
        cur = conn.cursor()
        pattern = f"%{(search_text or '').strip()}%"
        cur.execute(
            """
            SELECT id, IFNULL(code_patient, ''), nom, IFNULL(prenom, ''), IFNULL(telephone1, '')
            FROM patients
            WHERE nom LIKE ? OR prenom LIKE ? OR IFNULL(code_patient, '') LIKE ? OR IFNULL(telephone1, '') LIKE ?
            ORDER BY nom, prenom
            """,
            (pattern, pattern, pattern, pattern),
        )
        rows = cur.fetchall()
        conn.close()
        return rows

    def _fill_selector_combo(self, combo, rows, keep_id):
        combo.blockSignals(True)
        combo.clear()
        selected_index = -1
        for idx, (pid, code, nom, prenom, tel) in enumerate(rows):
            label = f"{nom} {prenom}".strip()
            extra = f" | {code}" if code else ""
            extra += f" | {tel}" if tel else ""
            combo.addItem(f"{label}{extra}", pid)
            if keep_id is not None and int(pid) == int(keep_id):
                selected_index = idx

        if selected_index >= 0:
            combo.setCurrentIndex(selected_index)
        elif combo.count() > 0:
            combo.setCurrentIndex(0)
        combo.blockSignals(False)


    def save_patient(self):
        if not self.nom_input.text().strip():
            QMessageBox.warning(self, "Validation", "Le nom du patient est obligatoire.")
            return

        code = self.code_input.text().strip()
        birth_date = self.birth_input.date()
        age_value = self._compute_age_from_qdate(birth_date)
        date_naissance_value = birth_date.toString("yyyy-MM-dd")

        conn = self._db()
        cur = conn.cursor()
        if not code:
            code = self._compute_next_patient_code(cur)

        cur.execute(
            """
            INSERT INTO patients(
                code_patient, dossier_patient, nom, prenom, age, date_naissance, sexe, telephone1, telephone2, adresse, couverture
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                code,
                self.dossier_input.text().strip() or None,
                self.nom_input.text().strip(),
                self.prenom_input.text().strip(),
                age_value,
                date_naissance_value,
                self.sexe_input.currentText(),
                self.tel1_input.text().strip(),
                self.tel2_input.text().strip(),
                self.adresse_input.text().strip(),
                self.couverture_input.currentText(),
            ),
        )
        patient_id = cur.lastrowid

        cur.execute(
            """
            INSERT OR IGNORE INTO patient_finance(patient_id, session_price, patient_share, cnam_share, advance_balance, total_advance_paid)
            VALUES (?, 0, 0, 0, 0, 0)
            """,
            (patient_id,),
        )

        login_code = (self.dossier_input.text().strip() or self.code_input.text().strip() or f"PAT{patient_id}")
        cur.execute(
            """
            INSERT OR IGNORE INTO patient_portal_access(patient_id, login_code, pin_code, active)
            VALUES (?, ?, '0000', 1)
            """,
            (patient_id, login_code),
        )

        conn.commit()
        conn.close()

        self.refresh()
        self.clear_form()
        QMessageBox.information(self, "Succes", "Patient enregistre avec succes.")

    def _fill_from_selection(self):
        row = self.table.currentRow()
        if row < 0:
            return
        patient_id = int(self.table.item(row, 0).text())
        self.selected_patient_id = patient_id
        self._set_combo_to_patient(patient_id)
        self._set_payment_combo_to_patient(patient_id)
        self._load_patient_by_id(patient_id)

    def _load_patient_by_id(self, patient_id):
        if not patient_id:
            return

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT
                IFNULL(code_patient, ''),
                IFNULL(dossier_patient, ''),
                nom,
                IFNULL(prenom, ''),
                IFNULL(date_naissance, ''),
                IFNULL(sexe, ''),
                IFNULL(telephone1, ''),
                IFNULL(telephone2, ''),
                IFNULL(adresse, ''),
                IFNULL(couverture, ''),
                IFNULL(m.medecin_traitant, ''),
                IFNULL(m.nature_seances, ''),
                IFNULL(m.nb_seances_programme, 0),
                IFNULL(m.duree_seance_minutes, 30),
                IFNULL(f.session_price, 0),
                IFNULL(f.patient_share, 0),
                IFNULL(f.cnam_share, 0),
                IFNULL(SUM(a.paid_amount - IFNULL(au.amount_used, 0)), 0),
                IFNULL(f.advance_balance, 0),
                IFNULL(f.total_advance_paid, 0)
            FROM patients p
            LEFT JOIN medical_records m ON m.patient_id = p.id
            LEFT JOIN patient_finance f ON f.patient_id = p.id
            LEFT JOIN appointments a ON a.patient_id = p.id
            LEFT JOIN advance_usage au ON au.appointment_id = a.id
            WHERE p.id=?
            GROUP BY p.id
            """,
            (patient_id,),
        )
        row = cur.fetchone()
        conn.close()
        if not row:
            return

        (
            code,
            dossier,
            nom,
            prenom,
            birth_text,
            sexe,
            tel1,
            tel2,
            adresse,
            couverture,
            medecin,
            nature,
            nb_seances,
            duree,
            session_price,
            patient_share,
            cnam_share,
            total_paid,
            advance_balance,
            total_advance,
        ) = row

        self.edit_code_input.setText(code)
        self.edit_dossier_input.setText(dossier)
        self.edit_nom_input.setText(nom)
        self.edit_prenom_input.setText(prenom)
        birth_qdate = QDate.fromString(birth_text, "yyyy-MM-dd") if birth_text else QDate()
        if not birth_qdate.isValid():
            birth_qdate = QDate.currentDate().addYears(-30)
        self.edit_birth_input.setDate(birth_qdate)
        self._update_edit_age_label()
        self.edit_sexe_input.setCurrentText(sexe)
        self.edit_tel1_input.setText(tel1)
        self.edit_tel2_input.setText(tel2)
        self.edit_adresse_input.setText(adresse)
        self.edit_couverture_input.setCurrentText(couverture)
        self.edit_medecin_input.setText(medecin)

        self.total_paid_label.setText(f"{float(total_paid or 0):.2f}")
        self.total_advance_label.setText(f"{float(total_advance or 0):.2f}")
        self.advance_balance_label.setText(f"{float(advance_balance or 0):.2f}")
        self.payment_selected_label.setText(f"Patient selectionne: {nom} {prenom}".strip())
        self.refresh_payment_views()

    def _refresh_patient_selector(self, search_text="", keep_id=None):
        if keep_id is None:
            keep_id = self.selected_patient_id
        rows = self._fetch_patient_selector_rows(search_text)
        self._fill_selector_combo(self.patient_combo, rows, keep_id)

    def _refresh_program_selector(self, search_text="", keep_id=None):
        if keep_id is None:
            keep_id = self.selected_patient_id
        rows = self._fetch_patient_selector_rows(search_text)
        self._fill_selector_combo(self.program_patient_combo, rows, keep_id)

    def _refresh_payment_selector(self, search_text="", keep_id=None):
        if keep_id is None:
            keep_id = self.selected_patient_id
        rows = self._fetch_patient_selector_rows(search_text)
        self._fill_selector_combo(self.payment_patient_combo, rows, keep_id)

    def _set_combo_to_patient(self, patient_id):
        self.patient_combo.blockSignals(True)
        for idx in range(self.patient_combo.count()):
            if self.patient_combo.itemData(idx) == patient_id:
                self.patient_combo.setCurrentIndex(idx)
                break
        self.patient_combo.blockSignals(False)

    def _set_payment_combo_to_patient(self, patient_id):
        self.payment_patient_combo.blockSignals(True)
        for idx in range(self.payment_patient_combo.count()):
            if self.payment_patient_combo.itemData(idx) == patient_id:
                self.payment_patient_combo.setCurrentIndex(idx)
                break
        self.payment_patient_combo.blockSignals(False)

    def _on_patient_search_changed(self, text):
        self._refresh_patient_selector(search_text=text)
        if self.patient_combo.count() > 0:
            self._on_patient_combo_changed()

    def _on_payment_search_changed(self, text):
        self._refresh_payment_selector(search_text=text)
        if self.payment_patient_combo.count() > 0:
            self._on_payment_combo_changed()

    def _on_program_search_changed(self, text):
        self._refresh_program_selector(search_text=text)
        if self.program_patient_combo.count() > 0:
            self._on_program_patient_combo_changed()

    def _on_program_patient_combo_changed(self):
        patient_id = self.program_patient_combo.currentData()
        if patient_id is None:
            return
        self.selected_patient_id = int(patient_id)
        self._load_programs_for_patient(self.selected_patient_id)

    def _on_patient_combo_changed(self):
        patient_id = self.patient_combo.currentData()
        if patient_id is None:
            return
        self.selected_patient_id = int(patient_id)
        self._set_payment_combo_to_patient(self.selected_patient_id)
        self._load_patient_by_id(self.selected_patient_id)

    def _on_payment_combo_changed(self):
        patient_id = self.payment_patient_combo.currentData()
        if patient_id is None:
            return
        self.selected_patient_id = int(patient_id)
        self._set_combo_to_patient(self.selected_patient_id)
        self._load_patient_by_id(self.selected_patient_id)

    def _load_programs_for_patient(self, patient_id):
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT id, IFNULL(titre, ''), IFNULL(nature_seances, ''), IFNULL(nb_seances, 0),
                   IFNULL(duree_seance_minutes, 30), IFNULL(date_debut, ''), IFNULL(statut, 'planifie'),
                   IFNULL(session_price, 0), IFNULL(patient_share, 0), IFNULL(cnam_share, 0),
                   IFNULL(objectifs, ''), IFNULL(remarques, '')
            FROM patient_programs
            WHERE patient_id=?
            ORDER BY id DESC
            """,
            (patient_id,),
        )
        rows = cur.fetchall()
        conn.close()

        self.programs_table.setRowCount(len(rows))
        for r, row in enumerate(rows):
            for c, value in enumerate(row):
                self.programs_table.setItem(r, c, QTableWidgetItem(str(value)))
        self._clear_program_form()

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
        total_nb = sum(int(row[1] or 0) for row in rows)
        if not rows:
            return
        first = rows[0]
        cur.execute("SELECT IFNULL(medecin_traitant, '') FROM medical_records WHERE patient_id=?", (patient_id,))
        existing = cur.fetchone()
        medecin = existing[0] if existing else ""
        cur.execute("SELECT id FROM medical_records WHERE patient_id=?", (patient_id,))
        if cur.fetchone():
            cur.execute(
                """
                UPDATE medical_records
                SET medecin_traitant=?, nb_seances_programme=?, duree_seance_minutes=?, nature_seances=?, objectifs=?, remarques=?, updated_at=CURRENT_TIMESTAMP
                WHERE patient_id=?
                """,
                (
                    medecin,
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
                    medecin,
                    total_nb,
                    int(first[2] or 30),
                    first[0],
                    first[3],
                    first[4],
                ),
            )

    def save_program_for_patient(self):
        patient_id = self.program_patient_combo.currentData()
        if patient_id is None:
            QMessageBox.warning(self, "Validation", "Selectionnez un patient pour enregistrer le programme.")
            return

        title = self.program_title_edit.text().strip()
        nature = self.program_nature_edit.currentText().strip()
        nb = int(self.program_nb_edit.value())
        duration = int(self.program_duration_edit.value())
        start_date = self.program_start_edit.date().toString("yyyy-MM-dd")
        statut = self.program_status_combo.currentText()
        objectifs = self.program_objectifs_edit.toPlainText().strip()
        remarques = self.program_remarques_edit.toPlainText().strip()

        if not nature or nb <= 0:
            QMessageBox.warning(self, "Validation", "Nature et nombre de séances sont obligatoires.")
            return

        conn = self._db()
        cur = conn.cursor()
        cur.execute("INSERT OR IGNORE INTO session_types(libelle) VALUES (?)", (nature,))

        if getattr(self, "editing_program_id", None):
            cur.execute(
                """
                UPDATE patient_programs
                SET titre=?, nature_seances=?, nb_seances=?, duree_seance_minutes=?,
                    date_debut=?, statut=?, session_price=?, patient_share=?, cnam_share=?, objectifs=?, remarques=?
                WHERE id=? AND patient_id=?
                """,
                (
                    title,
                    nature,
                    nb,
                    duration,
                    start_date,
                    statut,
                    float(self.program_price_edit.value()),
                    float(self.program_patient_share_edit.value()),
                    float(self.program_cnam_share_edit.value()),
                    objectifs,
                    remarques,
                    int(self.editing_program_id),
                    patient_id,
                ),
            )
            saved_message = "Programme de séances modifié."
        else:
            cur.execute(
                """
                INSERT INTO patient_programs(
                    patient_id, titre, nature_seances, nb_seances, duree_seance_minutes,
                    date_debut, statut, session_price, patient_share, cnam_share, objectifs, remarques
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    patient_id,
                    title,
                    nature,
                    nb,
                    duration,
                    start_date,
                    statut,
                    float(self.program_price_edit.value()),
                    float(self.program_patient_share_edit.value()),
                    float(self.program_cnam_share_edit.value()),
                    objectifs,
                    remarques,
                ),
            )
            saved_message = "Programme de séances enregistré."

        self._sync_medical_record_from_programs(patient_id, cur)
        conn.commit()
        conn.close()

        self.editing_program_id = None
        self._load_programs_for_patient(patient_id)
        self._clear_program_form()
        QMessageBox.information(self, "Succes", saved_message)

    def _on_program_row_selected(self):
        row = self.programs_table.currentRow()
        if row < 0:
            return
        program_id_item = self.programs_table.item(row, 0)
        if not program_id_item:
            return
        self.editing_program_id = int(program_id_item.text())
        self.program_title_edit.setText(self.programs_table.item(row, 1).text())
        self.program_nature_edit.setCurrentText(self.programs_table.item(row, 2).text())
        self.program_nb_edit.setValue(int(self.programs_table.item(row, 3).text()))
        self.program_duration_edit.setValue(int(self.programs_table.item(row, 4).text()))
        date_text = self.programs_table.item(row, 5).text()
        self.program_start_edit.setDate(QDate.fromString(date_text, "yyyy-MM-dd") if date_text else QDate.currentDate())
        self.program_status_combo.setCurrentText(self.programs_table.item(row, 6).text())
        self.program_price_edit.setValue(float(self.programs_table.item(row, 7).text() or 0))
        self.program_patient_share_edit.setValue(float(self.programs_table.item(row, 8).text() or 0))
        self.program_cnam_share_edit.setValue(float(self.programs_table.item(row, 9).text() or 0))
        self.program_objectifs_edit.setPlainText(self.programs_table.item(row, 10).text())
        self.program_remarques_edit.setPlainText(self.programs_table.item(row, 11).text())

    def _clear_program_form(self):
        self.editing_program_id = None
        self.program_title_edit.clear()
        self.program_nature_edit.setCurrentIndex(0)
        self.program_nb_edit.setValue(0)
        self.program_duration_edit.setValue(30)
        self.program_start_edit.setDate(QDate.currentDate())
        self.program_status_combo.setCurrentIndex(0)
        self.program_price_edit.setValue(30)
        self.program_patient_share_edit.setValue(0)
        self.program_cnam_share_edit.setValue(0)
        self.program_objectifs_edit.clear()
        self.program_remarques_edit.clear()

    def delete_selected_program(self):
        row = self.programs_table.currentRow()
        if row < 0:
            QMessageBox.warning(self, "Validation", "Selectionnez un programme a supprimer.")
            return
        program_id_item = self.programs_table.item(row, 0)
        if not program_id_item:
            return
        program_id = int(program_id_item.text())

        reply = QMessageBox.question(
            self,
            "Confirmation",
            "Supprimer ce programme de séances ?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if reply != QMessageBox.StandardButton.Yes:
            return

        conn = self._db()
        cur = conn.cursor()
        cur.execute("DELETE FROM patient_programs WHERE id=?", (program_id,))
        self._sync_medical_record_from_programs(patient_id, cur)
        conn.commit()
        conn.close()

        if patient_id is not None:
            self._load_programs_for_patient(patient_id)

    def _current_selected_patient_id(self):
        if hasattr(self, "tabs") and hasattr(self, "program_tab") and self.tabs.currentWidget() == self.program_tab and hasattr(self, "program_patient_combo"):
            program_id = self.program_patient_combo.currentData()
            if program_id is not None:
                return int(program_id)
        if hasattr(self, "tabs") and self.tabs.currentIndex() == 3 and hasattr(self, "payment_patient_combo"):
            payment_id = self.payment_patient_combo.currentData()
            if payment_id is not None:
                return int(payment_id)
        if self.selected_patient_id is not None:
            return int(self.selected_patient_id)
        row = self.table.currentRow()
        if row >= 0 and self.table.item(row, 0):
            return int(self.table.item(row, 0).text())
        return None

    def update_selected_patient(self):
        patient_id = self._current_selected_patient_id()
        if patient_id is None:
            QMessageBox.warning(self, "Validation", "Selectionnez un patient a modifier.")
            return

        nature = self.edit_nature_input.currentText().strip()
        birth_date = self.edit_birth_input.date()
        age_value = self._compute_age_from_qdate(birth_date)
        date_naissance_value = birth_date.toString("yyyy-MM-dd")
        conn = self._db()
        cur = conn.cursor()

        if nature:
            cur.execute("INSERT OR IGNORE INTO session_types(libelle) VALUES (?)", (nature,))

        cur.execute(
            """
            UPDATE patients
            SET code_patient=?, dossier_patient=?, nom=?, prenom=?, age=?, date_naissance=?, sexe=?, telephone1=?, telephone2=?, adresse=?, couverture=?
            WHERE id=?
            """,
            (
                self.edit_code_input.text().strip() or None,
                self.edit_dossier_input.text().strip() or None,
                self.edit_nom_input.text().strip(),
                self.edit_prenom_input.text().strip(),
                age_value,
                date_naissance_value,
                self.edit_sexe_input.currentText(),
                self.edit_tel1_input.text().strip(),
                self.edit_tel2_input.text().strip(),
                self.edit_adresse_input.text().strip(),
                self.edit_couverture_input.currentText(),
                patient_id,
            ),
        )

        cur.execute("SELECT id FROM medical_records WHERE patient_id=?", (patient_id,))
        med = cur.fetchone()
        if med:
            cur.execute(
                """
                UPDATE medical_records
                SET medecin_traitant=?, updated_at=CURRENT_TIMESTAMP
                WHERE patient_id=?
                """,
                (
                    self.edit_medecin_input.text().strip(),
                    patient_id,
                ),
            )
        else:
            cur.execute(
                """
                INSERT INTO medical_records(patient_id, diagnostic, medecin_traitant, nb_seances_programme, duree_seance_minutes, nature_seances, objectifs, remarques)
                VALUES (?, '', ?, 0, 30, '', '', '')
                """,
                (
                    patient_id,
                    self.edit_medecin_input.text().strip(),
                ),
            )

        cur.execute(
            "INSERT OR IGNORE INTO patient_finance(patient_id, session_price, patient_share, cnam_share, advance_balance, total_advance_paid) VALUES (?, 0, 0, 0, 0, 0)",
            (patient_id,),
        )
        cur.execute(
            "UPDATE patient_finance SET session_price=?, patient_share=?, cnam_share=?, updated_at=CURRENT_TIMESTAMP WHERE patient_id=?",
            (
                float(self.edit_session_price_input.value()),
                float(self.edit_patient_share_input.value()),
                float(self.edit_cnam_share_input.value()),
                patient_id,
            ),
        )

        login_code = (self.edit_dossier_input.text().strip() or self.edit_code_input.text().strip() or f"PAT{patient_id}")
        cur.execute(
            "SELECT id FROM patient_portal_access WHERE patient_id=?",
            (patient_id,),
        )
        if cur.fetchone():
            cur.execute(
                "UPDATE patient_portal_access SET login_code=?, active=1 WHERE patient_id=?",
                (login_code, patient_id),
            )
        else:
            cur.execute(
                "INSERT INTO patient_portal_access(patient_id, login_code, pin_code, active) VALUES (?, ?, '0000', 1)",
                (patient_id, login_code),
            )

        conn.commit()
        conn.close()
        self.refresh()
        QMessageBox.information(self, "Succes", "Patient modifie avec succes.")

    def add_advance_for_selected(self):
        patient_id = self._current_selected_patient_id()
        if patient_id is None:
            QMessageBox.warning(self, "Validation", "Selectionnez un patient.")
            return
        amount = float(self.advance_add_input.value())
        if amount <= 0:
            QMessageBox.warning(self, "Validation", "Le montant d avance doit etre superieur a 0.")
            return

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            "INSERT OR IGNORE INTO patient_finance(patient_id, session_price, patient_share, cnam_share, advance_balance, total_advance_paid) VALUES (?, 0, 0, 0, 0, 0)",
            (patient_id,),
        )
        cur.execute(
            """
            UPDATE patient_finance
            SET advance_balance = IFNULL(advance_balance, 0) + ?,
                total_advance_paid = IFNULL(total_advance_paid, 0) + ?,
                updated_at=CURRENT_TIMESTAMP
            WHERE patient_id=?
            """,
            (amount, amount, patient_id),
        )
        register_advance_credit(cur, patient_id, amount, "Avance patient")
        conn.commit()
        conn.close()

        self.advance_add_input.setValue(0)
        self.refresh()
        QMessageBox.information(self, "Succes", "Avance ajoutee avec succes.")

    def refresh_payment_views(self):
        patient_id = self._current_selected_patient_id()
        if patient_id is None:
            self.advances_table.setRowCount(0)
            self.payment_state_table.setRowCount(0)
            return

        conn = self._db()
        cur = conn.cursor()
        projections = project_patient_payment_states(cur, patient_id)
        cur.execute(
            """
            SELECT transaction_date, amount, IFNULL(note, '')
            FROM advance_transactions
            WHERE patient_id=?
            ORDER BY transaction_date DESC
            """,
            (patient_id,),
        )
        advances = cur.fetchall()

        self.advances_table.setRowCount(len(advances))
        for r, row in enumerate(advances):
            for c, value in enumerate(row):
                if c == 1:
                    item = QTableWidgetItem(f"{float(value or 0):.2f}")
                else:
                    item = QTableWidgetItem(str(value))
                self.advances_table.setItem(r, c, item)

        cur.execute(
            """
             SELECT a.id,
                 date(a.start_datetime),
                   IFNULL(a.acte, ''),
                   IFNULL(a.status, 'planifie'),
                   IFNULL(a.payment_status, 'non_paye'),
                   IFNULL(a.amount, 0),
                   IFNULL(a.paid_amount, 0),
                   IFNULL(au.amount_used, 0)
            FROM appointments a
            LEFT JOIN advance_usage au ON au.appointment_id = a.id
            WHERE a.patient_id=?
            ORDER BY a.start_datetime DESC
            """,
            (patient_id,),
        )
        payments = cur.fetchall()
        conn.close()

        self.payment_state_table.setRowCount(len(payments))
        for r, row in enumerate(payments):
            appointment_id = int(row[0])
            projection = projections.get(appointment_id, {})
            display_row = [
                row[1],
                row[2],
                row[3],
                projection.get("payment_status", row[4]),
                float(row[5] or 0),
                float(projection.get("paid_total", row[6]) or 0),
                float(row[7] or 0) + float(projection.get("projected_advance", 0) or 0),
            ]
            for c, value in enumerate(display_row):
                if c >= 4:
                    item = QTableWidgetItem(f"{float(value or 0):.2f}")
                else:
                    item = QTableWidgetItem(str(value))
                self.payment_state_table.setItem(r, c, item)

    def clear_form(self):
        self._refresh_next_patient_code()
        self.dossier_input.clear()
        self.nom_input.clear()
        self.prenom_input.clear()
        self.birth_input.setDate(QDate.currentDate().addYears(-30))
        self._update_create_age_label()
        self.sexe_input.setCurrentIndex(0)
        self.tel1_input.clear()
        self.tel2_input.clear()
        self.adresse_input.clear()
        self.couverture_input.setCurrentIndex(0)

    def refresh(self):
        self._load_natures()
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT
                p.id,
                IFNULL(p.code_patient, ''),
                IFNULL(p.dossier_patient, ''),
                p.nom,
                IFNULL(p.prenom, ''),
                CASE
                    WHEN IFNULL(p.date_naissance, '') <> ''
                    THEN CAST((julianday('now') - julianday(p.date_naissance)) / 365.25 AS INTEGER)
                    ELSE IFNULL(p.age, 0)
                END,
                IFNULL(p.sexe, ''),
                IFNULL(p.telephone1, ''),
                IFNULL(p.couverture, ''),
                IFNULL(m.medecin_traitant, ''),
                IFNULL(m.nature_seances, ''),
                IFNULL(f.session_price, 0),
                IFNULL(SUM(a.paid_amount - IFNULL(au.amount_used, 0)), 0),
                IFNULL(f.advance_balance, 0),
                IFNULL(p.date_naissance, '')
            FROM patients p
            LEFT JOIN medical_records m ON m.patient_id = p.id
            LEFT JOIN patient_finance f ON f.patient_id = p.id
            LEFT JOIN appointments a ON a.patient_id = p.id
            LEFT JOIN advance_usage au ON au.appointment_id = a.id
            GROUP BY p.id
            ORDER BY p.nom, p.prenom
            """
        )
        rows = cur.fetchall()
        conn.close()

        self.table.setRowCount(len(rows))
        for r, row in enumerate(rows):
            for c, value in enumerate(row):
                if c in (11, 12, 13):
                    item = QTableWidgetItem(f"{float(value or 0):.2f}")
                else:
                    item = QTableWidgetItem(str(value))
                self.table.setItem(r, c, item)
        self.table.setColumnHidden(14, True)

        current_search = self.patient_search_input.text().strip() if hasattr(self, "patient_search_input") else ""
        self._refresh_patient_selector(search_text=current_search, keep_id=self.selected_patient_id)
        payment_search = self.payment_search_input.text().strip() if hasattr(self, "payment_search_input") else ""
        self._refresh_payment_selector(search_text=payment_search, keep_id=self.selected_patient_id)
        if hasattr(self, "program_search_input"):
            program_search = self.program_search_input.text().strip()
            self._refresh_program_selector(search_text=program_search, keep_id=self.selected_patient_id)
        if self.table.rowCount() > 0:
            if self.selected_patient_id is None:
                self.table.selectRow(0)
                self._fill_from_selection()
            else:
                self._load_patient_by_id(self.selected_patient_id)

        if hasattr(self, "longitudinal_tab"):
            self.longitudinal_tab.refresh()

        if hasattr(self, "code_input"):
            self._refresh_next_patient_code()
