import sqlite3

from PyQt6.QtCore import QDate
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QDateEdit, QTableWidget,
    QTableWidgetItem, QPushButton, QHeaderView, QAbstractItemView
)


class StatistiquesWidget(QWidget):
    def __init__(self, db_path):
        super().__init__()
        self.db_path = db_path
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

        title = QLabel("Statistiques du cabinet")
        title.setObjectName("sectionTitle")
        root.addWidget(title)

        top = QHBoxLayout()
        top.addWidget(QLabel("Période du"))
        self.start_date = QDateEdit(QDate.currentDate().addMonths(-1))
        self.start_date.setCalendarPopup(True)
        top.addWidget(self.start_date)

        top.addWidget(QLabel("au"))
        self.end_date = QDateEdit(QDate.currentDate())
        self.end_date.setCalendarPopup(True)
        top.addWidget(self.end_date)

        refresh_btn = QPushButton("Actualiser")
        refresh_btn.clicked.connect(self.refresh)
        top.addWidget(refresh_btn)
        top.addStretch()
        root.addLayout(top)

        self.kpi_label = QLabel()
        root.addWidget(self.kpi_label)

        self.by_kine_table = QTableWidget(0, 4)
        self.by_kine_table.setHorizontalHeaderLabels([
            "Kiné", "Nb séances", "Montant facturé", "Montant payé"
        ])
        self._decorate_table(self.by_kine_table)
        root.addWidget(self.by_kine_table)

        self.by_type_table = QTableWidget(0, 3)
        self.by_type_table.setHorizontalHeaderLabels([
            "Nature séance", "Nb séances", "CA"
        ])
        self._decorate_table(self.by_type_table)
        root.addWidget(self.by_type_table)

    def _db(self):
        return sqlite3.connect(self.db_path)

    def refresh(self):
        start = self.start_date.date().toString("yyyy-MM-dd")
        end = self.end_date.date().toString("yyyy-MM-dd")

        conn = self._db()
        cur = conn.cursor()

        cur.execute(
            """
            SELECT
                COUNT(*),
                IFNULL(SUM(amount), 0),
                IFNULL(SUM(paid_amount), 0),
                IFNULL(SUM(cnam_covered), 0),
                SUM(CASE WHEN status IN ('absent') THEN 1 ELSE 0 END)
            FROM appointments
            WHERE date(start_datetime) BETWEEN ? AND ?
            """,
            (start, end),
        )
        total_sessions, total_amount, total_paid, total_cnam, total_absent = cur.fetchone()

        self.kpi_label.setText(
            f"Séances: {int(total_sessions or 0)} | Facturé: {float(total_amount or 0):.2f} | "
            f"Payé: {float(total_paid or 0):.2f} | CNAM: {float(total_cnam or 0):.2f} | Absences: {int(total_absent or 0)}"
        )

        cur.execute(
            """
            SELECT IFNULL(u.full_name, u.username) AS kine,
                   COUNT(*),
                   IFNULL(SUM(a.amount), 0),
                   IFNULL(SUM(a.paid_amount), 0)
            FROM appointments a
            LEFT JOIN users u ON u.id = a.kine_id
            WHERE date(a.start_datetime) BETWEEN ? AND ?
            GROUP BY IFNULL(u.full_name, u.username)
            ORDER BY COUNT(*) DESC
            """,
            (start, end),
        )
        kine_rows = cur.fetchall()

        self.by_kine_table.setRowCount(len(kine_rows))
        for r, row in enumerate(kine_rows):
            for c, value in enumerate(row):
                self.by_kine_table.setItem(r, c, QTableWidgetItem(str(value)))

        cur.execute(
            """
            SELECT IFNULL(acte, 'Sans type') AS acte,
                   COUNT(*),
                   IFNULL(SUM(amount), 0)
            FROM appointments
            WHERE date(start_datetime) BETWEEN ? AND ?
            GROUP BY IFNULL(acte, 'Sans type')
            ORDER BY COUNT(*) DESC
            """,
            (start, end),
        )
        type_rows = cur.fetchall()

        self.by_type_table.setRowCount(len(type_rows))
        for r, row in enumerate(type_rows):
            for c, value in enumerate(row):
                self.by_type_table.setItem(r, c, QTableWidgetItem(str(value)))

        conn.close()
