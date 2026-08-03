import sqlite3

from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton, QComboBox,
    QListWidget, QListWidgetItem, QGroupBox, QTabWidget, QTableWidget,
    QTableWidgetItem, QLineEdit, QMessageBox, QHeaderView, QAbstractItemView
)


class SettingsWidget(QWidget):
    def __init__(
        self,
        db_path,
        current_theme,
        on_theme_change,
        on_refresh_current_tab,
        on_refresh_notifications,
        get_notifications,
    ):
        super().__init__()
        self.db_path = db_path
        self._on_theme_change = on_theme_change
        self._on_refresh_current_tab = on_refresh_current_tab
        self._on_refresh_notifications = on_refresh_notifications
        self._get_notifications = get_notifications
        self._build_ui(current_theme)
        self.reload_notifications()
        self.reload_session_types()

    def _decorate_table(self, table):
        table.setAlternatingRowColors(True)
        table.verticalHeader().setVisible(False)
        table.setShowGrid(False)
        table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        table.horizontalHeader().setStretchLastSection(True)

    def apply_permissions(self, permissions):
        permissions = permissions or {}
        if "parametres.actions" in permissions:
            self.tabs.setTabVisible(0, bool(permissions["parametres.actions"]))
        if "parametres.natures" in permissions:
            self.tabs.setTabVisible(1, bool(permissions["parametres.natures"]))

    def _db(self):
        return sqlite3.connect(self.db_path)

    def _build_ui(self, current_theme):
        root = QVBoxLayout(self)

        title = QLabel("Parametres")
        title.setObjectName("sectionTitle")
        root.addWidget(title)

        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        self._build_actions_tab(current_theme)
        self._build_session_types_tab()

    def _build_actions_tab(self, current_theme):
        tab = QWidget()
        root = QVBoxLayout(tab)

        actions_group = QGroupBox("Actions rapides")
        actions_layout = QHBoxLayout(actions_group)

        refresh_tab_btn = QPushButton("Rafraichir l onglet actuel")
        refresh_tab_btn.clicked.connect(self._on_refresh_current_tab)
        actions_layout.addWidget(refresh_tab_btn)

        theme_label = QLabel("Theme")
        actions_layout.addWidget(theme_label)

        self.theme_combo = QComboBox()
        self.theme_combo.addItems(["Clair", "Sombre", "Vert"])
        self.theme_combo.setCurrentText(current_theme if current_theme in ["Clair", "Sombre", "Vert"] else "Clair")
        self.theme_combo.currentTextChanged.connect(self._on_theme_change)
        actions_layout.addWidget(self.theme_combo)

        refresh_notif_btn = QPushButton("Rafraichir notifications")
        refresh_notif_btn.clicked.connect(self._refresh_and_reload_notifications)
        actions_layout.addWidget(refresh_notif_btn)

        actions_layout.addStretch()
        root.addWidget(actions_group)

        notif_group = QGroupBox("Notifications du jour")
        notif_layout = QVBoxLayout(notif_group)
        self.notifications_list = QListWidget()
        notif_layout.addWidget(self.notifications_list)
        root.addWidget(notif_group)

        self.tabs.addTab(tab, "General")

    def _build_session_types_tab(self):
        tab = QWidget()
        root = QVBoxLayout(tab)

        row = QHBoxLayout()
        row.addWidget(QLabel("Nouvelle nature"))
        self.new_type_input = QLineEdit()
        self.new_type_input.setPlaceholderText("Ex: Reeducation vestibulaire")
        row.addWidget(self.new_type_input, 1)
        add_btn = QPushButton("Ajouter")
        add_btn.clicked.connect(self.add_session_type)
        row.addWidget(add_btn)
        del_btn = QPushButton("Supprimer selection")
        del_btn.clicked.connect(self.delete_session_type)
        row.addWidget(del_btn)
        row.addStretch()
        root.addLayout(row)

        self.types_table = QTableWidget(0, 2)
        self.types_table.setHorizontalHeaderLabels(["ID", "Nature des seances"])
        self.types_table.setColumnHidden(0, True)
        self._decorate_table(self.types_table)
        root.addWidget(self.types_table)

        self.tabs.addTab(tab, "Natures des seances")

    def _refresh_and_reload_notifications(self):
        self._on_refresh_notifications()
        self.reload_notifications()

    def reload_notifications(self):
        self.notifications_list.clear()
        messages = self._get_notifications() or []
        if not messages:
            self.notifications_list.addItem(QListWidgetItem("Aucune notification."))
            return
        for message in messages:
            self.notifications_list.addItem(QListWidgetItem(message))

    def reload_session_types(self):
        conn = self._db()
        cur = conn.cursor()
        cur.execute("SELECT id, libelle FROM session_types ORDER BY libelle")
        rows = cur.fetchall()
        conn.close()

        self.types_table.setRowCount(len(rows))
        for r, (type_id, libelle) in enumerate(rows):
            self.types_table.setItem(r, 0, QTableWidgetItem(str(type_id)))
            self.types_table.setItem(r, 1, QTableWidgetItem(libelle))

    def add_session_type(self):
        value = self.new_type_input.text().strip()
        if not value:
            QMessageBox.warning(self, "Validation", "Saisissez une nature de seance.")
            return
        conn = self._db()
        cur = conn.cursor()
        cur.execute("INSERT OR IGNORE INTO session_types(libelle) VALUES (?)", (value,))
        conn.commit()
        conn.close()
        self.new_type_input.clear()
        self.reload_session_types()

    def delete_session_type(self):
        row = self.types_table.currentRow()
        if row < 0 or not self.types_table.item(row, 0):
            QMessageBox.warning(self, "Validation", "Selectionnez une nature a supprimer.")
            return
        type_id = int(self.types_table.item(row, 0).text())
        conn = self._db()
        cur = conn.cursor()
        cur.execute("DELETE FROM session_types WHERE id=?", (type_id,))
        conn.commit()
        conn.close()
        self.reload_session_types()
