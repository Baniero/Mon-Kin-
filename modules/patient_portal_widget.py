import sqlite3

from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QFormLayout, QLabel, QLineEdit,
    QPushButton, QTabWidget, QTableWidget, QTableWidgetItem, QTextEdit,
    QMessageBox, QComboBox, QHeaderView, QAbstractItemView
)


class PatientPortalWidget(QWidget):
    def __init__(self, db_path):
        super().__init__()
        self.db_path = db_path
        self.patient_id = None
        self.access_id = None
        self._build_ui()

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

        auth_box = QFormLayout()
        self.login_code = QLineEdit()
        self.pin_code = QLineEdit()
        self.pin_code.setEchoMode(QLineEdit.EchoMode.Password)
        login_btn = QPushButton("Se connecter (patient)")
        login_btn.clicked.connect(self.portal_login)
        auth_box.addRow("Code dossier/login", self.login_code)
        auth_box.addRow("PIN", self.pin_code)
        auth_box.addRow(login_btn)
        root.addLayout(auth_box)

        self.patient_label = QLabel("Patient: -")
        root.addWidget(self.patient_label)

        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        self._build_appointments_tab()
        self._build_documents_tab()
        self._build_finance_tab()
        self._build_questionnaire_tab()

    def _build_appointments_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)
        self.appt_table = QTableWidget(0, 5)
        self.appt_table.setHorizontalHeaderLabels(["ID", "Date", "Heure", "Acte", "Statut"])
        self.appt_table.setColumnHidden(0, True)
        self._decorate_table(self.appt_table)
        layout.addWidget(self.appt_table)

        row = QHBoxLayout()
        confirm_btn = QPushButton("Confirmer RDV")
        confirm_btn.clicked.connect(self.confirm_appointment)
        cancel_btn = QPushButton("Annuler RDV")
        cancel_btn.clicked.connect(self.cancel_appointment)
        row.addWidget(confirm_btn)
        row.addWidget(cancel_btn)
        row.addStretch()
        layout.addLayout(row)

        self.tabs.addTab(tab, "Rendez-vous")

    def _build_documents_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)
        self.docs_table = QTableWidget(0, 4)
        self.docs_table.setHorizontalHeaderLabels(["Date", "Categorie", "Fichier", "Note"])
        self._decorate_table(self.docs_table)
        layout.addWidget(self.docs_table)
        self.tabs.addTab(tab, "Documents")

    def _build_finance_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)
        self.balance_label = QLabel("Solde avance: 0.00")
        layout.addWidget(self.balance_label)
        self.ledger_table = QTableWidget(0, 5)
        self.ledger_table.setHorizontalHeaderLabels(["Date", "Type", "Montant", "Reference", "Note"])
        self._decorate_table(self.ledger_table)
        layout.addWidget(self.ledger_table)
        self.tabs.addTab(tab, "Finance")

    def _build_questionnaire_tab(self):
        tab = QWidget()
        form = QFormLayout(tab)
        self.q_appt_combo = QComboBox()
        self.q_douleur = QComboBox()
        self.q_douleur.addItems([str(i) for i in range(0, 11)])
        self.q_mobilite = QComboBox()
        self.q_mobilite.addItems([str(i) for i in range(0, 11)])
        self.q_gene = QComboBox()
        self.q_gene.addItems([str(i) for i in range(0, 11)])
        self.q_comment = QTextEdit()
        self.q_comment.setMaximumHeight(80)
        save_btn = QPushButton("Envoyer questionnaire")
        save_btn.clicked.connect(self.submit_questionnaire)

        form.addRow("Seance", self.q_appt_combo)
        form.addRow("Douleur (0-10)", self.q_douleur)
        form.addRow("Mobilite (0-10)", self.q_mobilite)
        form.addRow("Gene (0-10)", self.q_gene)
        form.addRow("Commentaire", self.q_comment)
        form.addRow(save_btn)

        self.tabs.addTab(tab, "Questionnaire pre-seance")

    def portal_login(self):
        code = self.login_code.text().strip()
        pin = self.pin_code.text().strip()
        if not code or not pin:
            QMessageBox.warning(self, "Validation", "Code et PIN obligatoires.")
            return

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT a.id, a.patient_id, p.nom, IFNULL(p.prenom, '')
            FROM patient_portal_access a
            JOIN patients p ON p.id = a.patient_id
            WHERE a.login_code=? AND a.pin_code=? AND IFNULL(a.active,1)=1
            """,
            (code, pin),
        )
        row = cur.fetchone()
        conn.close()
        if not row:
            QMessageBox.warning(self, "Connexion", "Acces invalide.")
            return

        self.access_id = int(row[0])
        self.patient_id = int(row[1])
        self.patient_label.setText(f"Patient: {row[2]} {row[3]}")
        self.refresh()

    def _selected_appointment_id(self):
        r = self.appt_table.currentRow()
        if r < 0 or not self.appt_table.item(r, 0):
            return None
        return int(self.appt_table.item(r, 0).text())

    def confirm_appointment(self):
        appointment_id = self._selected_appointment_id()
        if appointment_id is None:
            return
        conn = self._db()
        cur = conn.cursor()
        cur.execute("UPDATE appointments SET status='confirme_patient' WHERE id=?", (appointment_id,))
        conn.commit()
        conn.close()
        self.refresh()

    def cancel_appointment(self):
        appointment_id = self._selected_appointment_id()
        if appointment_id is None:
            return
        conn = self._db()
        cur = conn.cursor()
        cur.execute("UPDATE appointments SET status='annule_patient' WHERE id=?", (appointment_id,))
        conn.commit()
        conn.close()
        self.refresh()

    def submit_questionnaire(self):
        if self.patient_id is None:
            QMessageBox.warning(self, "Validation", "Connectez-vous d abord.")
            return
        appt_id = self.q_appt_combo.currentData()
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            INSERT INTO patient_questionnaires(patient_id, appointment_id, douleur, mobilite, gene, commentaire)
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            (
                self.patient_id,
                int(appt_id) if appt_id is not None else None,
                int(self.q_douleur.currentText()),
                int(self.q_mobilite.currentText()),
                int(self.q_gene.currentText()),
                self.q_comment.toPlainText().strip(),
            ),
        )
        conn.commit()
        conn.close()
        self.q_comment.clear()
        QMessageBox.information(self, "Succes", "Questionnaire envoye.")

    def refresh(self):
        if self.patient_id is None:
            self.appt_table.setRowCount(0)
            self.docs_table.setRowCount(0)
            self.ledger_table.setRowCount(0)
            self.q_appt_combo.clear()
            self.balance_label.setText("Solde avance: 0.00")
            return

        conn = self._db()
        cur = conn.cursor()

        cur.execute(
            """
            SELECT id, date(start_datetime), strftime('%H:%M', start_datetime), IFNULL(acte, ''), IFNULL(status, 'planifie')
            FROM appointments
            WHERE patient_id=?
            ORDER BY start_datetime DESC
            """,
            (self.patient_id,),
        )
        appts = cur.fetchall()
        self.appt_table.setRowCount(len(appts))
        self.q_appt_combo.clear()
        for r, row in enumerate(appts):
            self.q_appt_combo.addItem(f"{row[1]} {row[2]} - {row[3]}", int(row[0]))
            for c, value in enumerate(row):
                self.appt_table.setItem(r, c, QTableWidgetItem(str(value)))

        cur.execute(
            "SELECT created_at, IFNULL(category, ''), file_path, IFNULL(note, '') FROM patient_attachments WHERE patient_id=? ORDER BY id DESC",
            (self.patient_id,),
        )
        docs = cur.fetchall()
        self.docs_table.setRowCount(len(docs))
        for r, row in enumerate(docs):
            for c, value in enumerate(row):
                self.docs_table.setItem(r, c, QTableWidgetItem(str(value)))

        cur.execute(
            "SELECT created_at, entry_type, amount, IFNULL(reference, ''), IFNULL(note, '') FROM finance_ledger WHERE patient_id=? ORDER BY id DESC",
            (self.patient_id,),
        )
        ledger = cur.fetchall()
        self.ledger_table.setRowCount(len(ledger))
        for r, row in enumerate(ledger):
            for c, value in enumerate(row):
                if c == 2:
                    self.ledger_table.setItem(r, c, QTableWidgetItem(f"{float(value or 0):.2f}"))
                else:
                    self.ledger_table.setItem(r, c, QTableWidgetItem(str(value)))

        cur.execute("SELECT IFNULL(advance_balance, 0) FROM patient_finance WHERE patient_id=?", (self.patient_id,))
        bal = cur.fetchone()
        self.balance_label.setText(f"Solde avance: {float(bal[0] if bal else 0):.2f}")

        conn.close()
