from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QLineEdit, QPushButton,
    QTabWidget, QFormLayout, QGroupBox, QTableWidget, QTableWidgetItem,
    QMessageBox, QComboBox, QScrollArea, QCheckBox, QHeaderView, QAbstractItemView
)

from db import (
    set_setting,
    get_setting,
    upsert_user,
    deactivate_user as deactivate_user_db,
    get_db_path,
    get_user_permissions,
    set_user_permissions,
)


ACCESS_SECTIONS = [
    ("tab.patients", "Onglet Patients"),
    ("patients.create", "Patients > Creation"),
    ("patients.edit", "Patients > Modification"),
    ("patients.payment", "Patients > Etat paiements et avances"),
    ("patients.longitudinal", "Patients > Dossier longitudinal"),
    ("tab.rendezvous", "Onglet Rendez-vous"),
    ("rendezvous.fix", "Rendez-vous > Fixation RDV"),
    ("rendezvous.week", "Rendez-vous > Planning hebdomadaire"),
    ("rendezvous.day", "Rendez-vous > Planning journalier"),
    ("rendezvous.month", "Rendez-vous > Calendrier mensuel"),
    ("rendezvous.charge", "Rendez-vous > Charge kine"),
    ("tab.caisse", "Onglet Caisse"),
    ("tab.statistiques", "Onglet Statistiques"),
    ("tab.portail_patient", "Onglet Portail patient"),
    ("tab.parametres", "Onglet Parametres"),
    ("parametres.actions", "Parametres > General"),
    ("parametres.natures", "Parametres > Natures des seances"),
    ("tab.utilisateurs", "Onglet Gestion utilisateurs"),
]


class UsersWidget(QWidget):
    def __init__(self, db_path, on_cabinet_name_changed=None):
        super().__init__()
        self.db_path = db_path
        self.on_cabinet_name_changed = on_cabinet_name_changed
        self.permission_checks = {}
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

    def _build_ui(self):
        root = QVBoxLayout(self)

        title = QLabel("Gestion utilisateurs et parametres cabinet")
        title.setObjectName("sectionTitle")
        root.addWidget(title)

        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        self._build_kine_tab()
        self._build_cabinet_tab()
        self._build_access_tab()

    def _build_kine_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)

        form_box = QGroupBox("Creer / modifier un kine")
        form = QFormLayout(form_box)

        self.username_input = QLineEdit()
        self.password_input = QLineEdit()
        self.password_input.setPlaceholderText("Laisser vide pour ne pas changer")
        self.fullname_input = QLineEdit()
        self.role_input = QComboBox()
        self.role_input.addItems(["kine", "admin"])

        form.addRow("Nom utilisateur", self.username_input)
        form.addRow("Mot de passe", self.password_input)
        form.addRow("Nom complet", self.fullname_input)
        form.addRow("Role", self.role_input)

        buttons = QHBoxLayout()
        save_btn = QPushButton("Enregistrer")
        save_btn.clicked.connect(self.save_user)
        deactivate_btn = QPushButton("Desactiver")
        deactivate_btn.clicked.connect(self.deactivate_user)
        buttons.addWidget(save_btn)
        buttons.addWidget(deactivate_btn)
        form.addRow(buttons)

        layout.addWidget(form_box)

        self.users_table = QTableWidget(0, 5)
        self.users_table.setHorizontalHeaderLabels(["ID", "Username", "Nom complet", "Role", "Actif"])
        self.users_table.setColumnHidden(0, True)
        self._decorate_table(self.users_table)
        self.users_table.itemSelectionChanged.connect(self.fill_form_from_selection)
        layout.addWidget(self.users_table)

        self.tabs.addTab(tab, "Utilisateurs / Kines")

    def _build_cabinet_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)

        box = QGroupBox("Nom du cabinet")
        form = QFormLayout(box)

        self.cabinet_name_input = QLineEdit()
        self.cabinet_name_input.setText(get_setting("cabinet_name", "Le cabinet de kinesitherapie et de reeducation"))

        save_btn = QPushButton("Mettre a jour")
        save_btn.clicked.connect(self.save_cabinet_name)

        form.addRow("Nom affiche", self.cabinet_name_input)
        form.addRow(save_btn)

        layout.addWidget(box)
        layout.addStretch()

        self.tabs.addTab(tab, "Cabinet")

    def _build_access_tab(self):
        tab = QWidget()
        layout = QVBoxLayout(tab)

        top = QHBoxLayout()
        top.addWidget(QLabel("Utilisateur cible"))
        self.access_user_combo = QComboBox()
        self.access_user_combo.currentIndexChanged.connect(self._load_permissions_for_selected_user)
        top.addWidget(self.access_user_combo, 1)

        load_btn = QPushButton("Recharger")
        load_btn.clicked.connect(self._load_permissions_for_selected_user)
        top.addWidget(load_btn)
        save_btn = QPushButton("Enregistrer droits")
        save_btn.clicked.connect(self._save_permissions_for_selected_user)
        top.addWidget(save_btn)
        top.addStretch()
        layout.addLayout(top)

        container = QWidget()
        checks_layout = QVBoxLayout(container)
        for section_key, label in ACCESS_SECTIONS:
            check = QCheckBox(label)
            check.setChecked(True)
            checks_layout.addWidget(check)
            self.permission_checks[section_key] = check
        checks_layout.addStretch()

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(container)
        layout.addWidget(scroll)

        self.tabs.addTab(tab, "Droits acces")

    def _db(self):
        import sqlite3
        return sqlite3.connect(get_db_path())

    def fill_form_from_selection(self):
        row = self.users_table.currentRow()
        if row < 0:
            return

        self.username_input.setText(self.users_table.item(row, 1).text())
        self.fullname_input.setText(self.users_table.item(row, 2).text())
        self.role_input.setCurrentText(self.users_table.item(row, 3).text())

    def save_user(self):
        username = self.username_input.text().strip()
        password = self.password_input.text().strip()
        full_name = self.fullname_input.text().strip()
        role = self.role_input.currentText().strip()

        if not username:
            QMessageBox.warning(self, "Validation", "Le nom utilisateur est obligatoire.")
            return

        try:
            upsert_user(
                username=username,
                role=role,
                full_name=full_name,
                password=password or None,
            )
        except ValueError:
            QMessageBox.warning(self, "Validation", "Le mot de passe est obligatoire pour une creation.")
            return

        self.password_input.clear()
        self.refresh()
        QMessageBox.information(self, "Succes", "Utilisateur enregistre.")

    def deactivate_user(self):
        username = self.username_input.text().strip()
        if not username:
            QMessageBox.warning(self, "Validation", "Selectionnez un utilisateur a desactiver.")
            return

        deactivate_user_db(username)

        self.refresh()
        QMessageBox.information(self, "Succes", "Utilisateur desactive.")

    def save_cabinet_name(self):
        name = self.cabinet_name_input.text().strip()
        if not name:
            QMessageBox.warning(self, "Validation", "Le nom du cabinet est obligatoire.")
            return

        set_setting("cabinet_name", name)
        if self.on_cabinet_name_changed:
            self.on_cabinet_name_changed()
        QMessageBox.information(self, "Succes", "Nom du cabinet mis a jour.")

    def _load_permissions_for_selected_user(self):
        username = self.access_user_combo.currentData()
        if not username:
            return

        perms = get_user_permissions(username)
        for key, _label in ACCESS_SECTIONS:
            allowed = perms.get(key, True)
            self.permission_checks[key].setChecked(bool(allowed))

    def _save_permissions_for_selected_user(self):
        username = self.access_user_combo.currentData()
        if not username:
            QMessageBox.warning(self, "Validation", "Choisissez un utilisateur.")
            return

        perms = {key: bool(check.isChecked()) for key, check in self.permission_checks.items()}
        set_user_permissions(username, perms)
        QMessageBox.information(self, "Succes", "Droits enregistres.")

    def refresh(self):
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT id, username, IFNULL(full_name, ''), role, active
            FROM users
            ORDER BY active DESC, role, username
            """
        )
        rows = cur.fetchall()
        conn.close()

        self.users_table.setRowCount(len(rows))
        self.access_user_combo.clear()
        for r, row in enumerate(rows):
            formatted = list(row)
            username = formatted[1]
            formatted[4] = "Oui" if int(formatted[4]) == 1 else "Non"
            for c, value in enumerate(formatted):
                self.users_table.setItem(r, c, QTableWidgetItem(str(value)))
            self.access_user_combo.addItem(f"{username} ({formatted[3]})", username)

        if self.access_user_combo.count() > 0:
            self._load_permissions_for_selected_user()
