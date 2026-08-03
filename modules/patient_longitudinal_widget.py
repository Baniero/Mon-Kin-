import sqlite3

from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QFormLayout, QLabel, QLineEdit,
    QComboBox, QPushButton, QTabWidget, QTableWidget, QTableWidgetItem,
    QTextEdit, QFileDialog, QMessageBox, QGroupBox, QDateEdit,
    QHeaderView, QAbstractItemView
)
from PyQt6.QtCore import QDate


class PatientLongitudinalWidget(QWidget):
    def __init__(self, db_path):
        super().__init__()
        self.db_path = db_path
        self.selected_patient_id = None
        self._build_ui()
        self.refresh()

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

    def _build_ui(self):
        root = QVBoxLayout(self)

        selector = QHBoxLayout()
        selector.addWidget(QLabel("Recherche patient"))
        self.search_input = QLineEdit()
        self.search_input.setPlaceholderText("Nom, prenom, code, dossier, telephone")
        self.search_input.textChanged.connect(self._on_search)
        selector.addWidget(self.search_input, 2)

        selector.addWidget(QLabel("Patient"))
        self.patient_combo = QComboBox()
        self.patient_combo.currentIndexChanged.connect(self._on_patient_changed)
        selector.addWidget(self.patient_combo, 2)
        root.addLayout(selector)

        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        self._build_timeline_tab()
        self._build_alerts_tab()
        self._build_attachments_tab()
        self._build_programs_tab()

    def _build_timeline_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)

        form_box = QGroupBox("Ajouter evenement longitudinal")
        form = QFormLayout(form_box)
        self.timeline_type = QComboBox()
        self.timeline_type.addItems(["evaluation_initiale", "objectif", "bilan_intermediaire", "seance", "resultat_sortie"])
        self.timeline_date = QDateEdit(QDate.currentDate())
        self.timeline_date.setCalendarPopup(True)
        self.timeline_title = QLineEdit()
        self.timeline_details = QTextEdit()
        self.timeline_details.setMaximumHeight(80)

        form.addRow("Type", self.timeline_type)
        form.addRow("Date", self.timeline_date)
        form.addRow("Titre", self.timeline_title)
        form.addRow("Details", self.timeline_details)
        add_btn = QPushButton("Ajouter evenement")
        add_btn.clicked.connect(self.add_timeline_event)
        form.addRow(add_btn)

        layout.addWidget(form_box)

        self.timeline_table = QTableWidget(0, 4)
        self.timeline_table.setHorizontalHeaderLabels(["Date", "Type", "Titre", "Details"])
        self._decorate_table(self.timeline_table)
        layout.addWidget(self.timeline_table)

        self.tabs.addTab(tab, "Timeline")

    def _build_alerts_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)

        box = QGroupBox("Alertes cliniques")
        form = QFormLayout(box)
        self.alert_type = QComboBox()
        self.alert_type.addItems(["allergie", "contre_indication", "drapeau_rouge"])
        self.alert_severity = QComboBox()
        self.alert_severity.addItems(["faible", "moyen", "eleve"])
        self.alert_content = QLineEdit()
        self.alert_content.setPlaceholderText("Contenu alerte")
        add_btn = QPushButton("Ajouter alerte")
        add_btn.clicked.connect(self.add_alert)
        form.addRow("Type", self.alert_type)
        form.addRow("Gravite", self.alert_severity)
        form.addRow("Contenu", self.alert_content)
        form.addRow(add_btn)

        layout.addWidget(box)

        self.alerts_table = QTableWidget(0, 4)
        self.alerts_table.setHorizontalHeaderLabels(["ID", "Type", "Gravite", "Contenu"])
        self.alerts_table.setColumnHidden(0, True)
        self._decorate_table(self.alerts_table)
        layout.addWidget(self.alerts_table)

        deactivate_btn = QPushButton("Desactiver alerte selectionnee")
        deactivate_btn.clicked.connect(self.deactivate_alert)
        layout.addWidget(deactivate_btn)

        self.tabs.addTab(tab, "Alertes")

    def _build_attachments_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)

        box = QGroupBox("Pieces jointes")
        form = QFormLayout(box)
        self.attach_category = QComboBox()
        self.attach_category.addItems(["IRM", "radio", "ordonnance", "consentement", "autre"])
        self.attach_path = QLineEdit()
        browse_btn = QPushButton("Parcourir")
        browse_btn.clicked.connect(self.pick_attachment)
        path_row = QHBoxLayout()
        path_row.addWidget(self.attach_path)
        path_row.addWidget(browse_btn)
        path_widget = QWidget()
        path_widget.setLayout(path_row)
        self.attach_note = QLineEdit()
        add_btn = QPushButton("Ajouter piece jointe")
        add_btn.clicked.connect(self.add_attachment)
        form.addRow("Categorie", self.attach_category)
        form.addRow("Fichier", path_widget)
        form.addRow("Note", self.attach_note)
        form.addRow(add_btn)

        layout.addWidget(box)

        self.attach_table = QTableWidget(0, 4)
        self.attach_table.setHorizontalHeaderLabels(["Date", "Categorie", "Chemin", "Note"])
        self._decorate_table(self.attach_table)
        layout.addWidget(self.attach_table)

        self.tabs.addTab(tab, "Pieces jointes")

    def _build_programs_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)

        self.programs_table = QTableWidget(0, 6)
        self.programs_table.setHorizontalHeaderLabels(["ID", "Titre", "Nature", "Nb", "Debut", "Statut"])
        self.programs_table.setColumnHidden(0, True)
        self._decorate_table(self.programs_table)
        layout.addWidget(self.programs_table)

        row = QHBoxLayout()
        row.addWidget(QLabel("Nouveau statut"))
        self.program_status_combo = QComboBox()
        self.program_status_combo.addItems(["planifie", "en_cours", "termine", "suspendu", "archive"])
        row.addWidget(self.program_status_combo)
        upd_btn = QPushButton("Mettre a jour statut")
        upd_btn.clicked.connect(self.update_program_status)
        row.addWidget(upd_btn)
        row.addStretch()
        layout.addLayout(row)

        self.tabs.addTab(tab, "Programmes")

    def _load_patient_selector(self, search_text=""):
        conn = self._db()
        cur = conn.cursor()
        pattern = f"%{(search_text or '').strip()}%"
        cur.execute(
            """
            SELECT id, IFNULL(code_patient, ''), IFNULL(dossier_patient, ''), nom, IFNULL(prenom, ''), IFNULL(telephone1, '')
            FROM patients
            WHERE nom LIKE ? OR prenom LIKE ? OR IFNULL(code_patient, '') LIKE ? OR IFNULL(dossier_patient, '') LIKE ? OR IFNULL(telephone1, '') LIKE ?
            ORDER BY nom, prenom
            """,
            (pattern, pattern, pattern, pattern, pattern),
        )
        rows = cur.fetchall()
        conn.close()

        keep = self.selected_patient_id
        self.patient_combo.blockSignals(True)
        self.patient_combo.clear()
        idx_keep = -1
        for i, (pid, code, dossier, nom, prenom, tel) in enumerate(rows):
            label = f"{nom} {prenom}".strip()
            if code:
                label += f" | {code}"
            if dossier:
                label += f" | Dossier {dossier}"
            if tel:
                label += f" | {tel}"
            self.patient_combo.addItem(label, int(pid))
            if keep is not None and int(pid) == int(keep):
                idx_keep = i
        if idx_keep >= 0:
            self.patient_combo.setCurrentIndex(idx_keep)
        elif self.patient_combo.count() > 0:
            self.patient_combo.setCurrentIndex(0)
        self.patient_combo.blockSignals(False)

    def _on_search(self, text):
        self._load_patient_selector(text)
        if self.patient_combo.count() > 0:
            self._on_patient_changed()

    def _on_patient_changed(self):
        pid = self.patient_combo.currentData()
        if pid is None:
            return
        self.selected_patient_id = int(pid)
        self.refresh_tables()

    def add_timeline_event(self):
        if self.selected_patient_id is None:
            QMessageBox.warning(self, "Validation", "Selectionnez un patient.")
            return
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            INSERT INTO patient_timeline(patient_id, event_type, event_date, title, details)
            VALUES (?, ?, ?, ?, ?)
            """,
            (
                self.selected_patient_id,
                self.timeline_type.currentText(),
                self.timeline_date.date().toString("yyyy-MM-dd"),
                self.timeline_title.text().strip(),
                self.timeline_details.toPlainText().strip(),
            ),
        )
        conn.commit()
        conn.close()
        self.timeline_title.clear()
        self.timeline_details.clear()
        self.refresh_tables()

    def add_alert(self):
        if self.selected_patient_id is None:
            QMessageBox.warning(self, "Validation", "Selectionnez un patient.")
            return
        content = self.alert_content.text().strip()
        if not content:
            QMessageBox.warning(self, "Validation", "Contenu alerte obligatoire.")
            return
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            "INSERT INTO patient_alerts(patient_id, alert_type, severity, content, active) VALUES (?, ?, ?, ?, 1)",
            (self.selected_patient_id, self.alert_type.currentText(), self.alert_severity.currentText(), content),
        )
        conn.commit()
        conn.close()
        self.alert_content.clear()
        self.refresh_tables()

    def deactivate_alert(self):
        row = self.alerts_table.currentRow()
        if row < 0 or not self.alerts_table.item(row, 0):
            return
        alert_id = int(self.alerts_table.item(row, 0).text())
        conn = self._db()
        cur = conn.cursor()
        cur.execute("UPDATE patient_alerts SET active=0 WHERE id=?", (alert_id,))
        conn.commit()
        conn.close()
        self.refresh_tables()

    def pick_attachment(self):
        path, _ = QFileDialog.getOpenFileName(self, "Choisir piece jointe", "", "All Files (*.*)")
        if path:
            self.attach_path.setText(path)

    def add_attachment(self):
        if self.selected_patient_id is None:
            QMessageBox.warning(self, "Validation", "Selectionnez un patient.")
            return
        path = self.attach_path.text().strip()
        if not path:
            QMessageBox.warning(self, "Validation", "Choisissez un fichier.")
            return
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            "INSERT INTO patient_attachments(patient_id, category, file_path, note) VALUES (?, ?, ?, ?)",
            (self.selected_patient_id, self.attach_category.currentText(), path, self.attach_note.text().strip()),
        )
        conn.commit()
        conn.close()
        self.attach_path.clear()
        self.attach_note.clear()
        self.refresh_tables()

    def update_program_status(self):
        row = self.programs_table.currentRow()
        if row < 0 or not self.programs_table.item(row, 0):
            return
        program_id = int(self.programs_table.item(row, 0).text())
        status = self.program_status_combo.currentText()
        conn = self._db()
        cur = conn.cursor()
        cur.execute("UPDATE patient_programs SET statut=? WHERE id=?", (status, program_id))
        conn.commit()
        conn.close()
        self.refresh_tables()

    def refresh_tables(self):
        if self.selected_patient_id is None:
            self.timeline_table.setRowCount(0)
            self.alerts_table.setRowCount(0)
            self.attach_table.setRowCount(0)
            self.programs_table.setRowCount(0)
            return

        conn = self._db()
        cur = conn.cursor()

        cur.execute(
            "SELECT event_date, event_type, IFNULL(title, ''), IFNULL(details, '') FROM patient_timeline WHERE patient_id=? ORDER BY event_date DESC, id DESC",
            (self.selected_patient_id,),
        )
        timeline_rows = cur.fetchall()
        self.timeline_table.setRowCount(len(timeline_rows))
        for r, row in enumerate(timeline_rows):
            for c, value in enumerate(row):
                self.timeline_table.setItem(r, c, QTableWidgetItem(str(value)))

        cur.execute(
            "SELECT id, alert_type, severity, content FROM patient_alerts WHERE patient_id=? AND IFNULL(active,1)=1 ORDER BY id DESC",
            (self.selected_patient_id,),
        )
        alert_rows = cur.fetchall()
        self.alerts_table.setRowCount(len(alert_rows))
        for r, row in enumerate(alert_rows):
            for c, value in enumerate(row):
                self.alerts_table.setItem(r, c, QTableWidgetItem(str(value)))

        cur.execute(
            "SELECT created_at, IFNULL(category, ''), file_path, IFNULL(note, '') FROM patient_attachments WHERE patient_id=? ORDER BY id DESC",
            (self.selected_patient_id,),
        )
        attach_rows = cur.fetchall()
        self.attach_table.setRowCount(len(attach_rows))
        for r, row in enumerate(attach_rows):
            for c, value in enumerate(row):
                self.attach_table.setItem(r, c, QTableWidgetItem(str(value)))

        cur.execute(
            "SELECT id, IFNULL(titre, ''), IFNULL(nature_seances, ''), IFNULL(nb_seances, 0), IFNULL(date_debut, ''), IFNULL(statut, 'planifie') FROM patient_programs WHERE patient_id=? ORDER BY id DESC",
            (self.selected_patient_id,),
        )
        prog_rows = cur.fetchall()
        self.programs_table.setRowCount(len(prog_rows))
        for r, row in enumerate(prog_rows):
            for c, value in enumerate(row):
                self.programs_table.setItem(r, c, QTableWidgetItem(str(value)))

        conn.close()

    def refresh(self):
        self._load_patient_selector(self.search_input.text().strip() if hasattr(self, "search_input") else "")
        if self.patient_combo.count() > 0 and self.selected_patient_id is None:
            self._on_patient_changed()
        else:
            self.refresh_tables()
