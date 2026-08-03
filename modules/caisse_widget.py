import sqlite3

from PyQt6.QtCore import QDate
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QDateEdit, QDoubleSpinBox,
    QPushButton, QMessageBox, QTableWidget, QTableWidgetItem, QFileDialog,
    QHeaderView, QTabWidget, QAbstractItemView
)

from modules.export_utils import export_simple_table_pdf


class CaisseWidget(QWidget):
    def __init__(self, db_path, utilisateur):
        super().__init__()
        self.db_path = db_path
        self.utilisateur = utilisateur
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

        title = QLabel("Gestion de la caisse")
        title.setObjectName("sectionTitle")
        root.addWidget(title)

        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        self._build_daily_tab()
        self._build_monthly_tab()
        self._build_period_tab()
        self._build_cnam_tab()

    def _build_daily_tab(self):
        self.daily_tab = QWidget()
        root = QVBoxLayout(self.daily_tab)

        row = QHBoxLayout()
        row.addWidget(QLabel("Date"))
        self.daily_date_edit = QDateEdit(QDate.currentDate())
        self.daily_date_edit.setCalendarPopup(True)
        self.daily_date_edit.dateChanged.connect(self.update_expected_amount)
        row.addWidget(self.daily_date_edit)

        self.daily_expected_label = QLabel("Montant attendu: 0.00")
        row.addWidget(self.daily_expected_label)

        row.addWidget(QLabel("Montant réel"))
        self.daily_actual_amount = QDoubleSpinBox()
        self.daily_actual_amount.setRange(0, 100000)
        self.daily_actual_amount.setDecimals(2)
        row.addWidget(self.daily_actual_amount)

        validate_btn = QPushButton("Valider la caisse")
        validate_btn.clicked.connect(self.validate_cash)
        row.addWidget(validate_btn)

        export_btn = QPushButton("Exporter PDF")
        export_btn.clicked.connect(self.export_daily_pdf)
        row.addWidget(export_btn)
        row.addStretch()

        root.addLayout(row)

        self.daily_table = QTableWidget(0, 6)
        self.daily_table.setHorizontalHeaderLabels([
            "Date", "Attendu", "Réel", "Ecart", "Validé", "Utilisateur"
        ])
        self._decorate_table(self.daily_table)
        root.addWidget(self.daily_table)

        self.tabs.addTab(self.daily_tab, "Caisse journalière")

    def _build_monthly_tab(self):
        self.monthly_tab = QWidget()
        root = QVBoxLayout(self.monthly_tab)

        row = QHBoxLayout()
        row.addWidget(QLabel("Mois"))
        self.month_date_edit = QDateEdit(QDate.currentDate())
        self.month_date_edit.setCalendarPopup(True)
        self.month_date_edit.setDisplayFormat("MM/yyyy")
        self.month_date_edit.dateChanged.connect(self.refresh_monthly)
        row.addWidget(self.month_date_edit)

        self.month_expected_label = QLabel("Attendu mensuel: 0.00")
        self.month_actual_label = QLabel("Réel mensuel: 0.00")
        self.month_diff_label = QLabel("Ecart: 0.00")
        row.addWidget(self.month_expected_label)
        row.addWidget(self.month_actual_label)
        row.addWidget(self.month_diff_label)

        export_btn = QPushButton("Exporter PDF")
        export_btn.clicked.connect(self.export_monthly_pdf)
        row.addWidget(export_btn)
        row.addStretch()
        root.addLayout(row)

        self.monthly_table = QTableWidget(0, 6)
        self.monthly_table.setHorizontalHeaderLabels([
            "Date", "Attendu", "Réel", "Ecart", "Validé", "Utilisateur"
        ])
        self._decorate_table(self.monthly_table)
        root.addWidget(self.monthly_table)

        self.tabs.addTab(self.monthly_tab, "Caisse mensuelle")

    def _build_period_tab(self):
        self.period_tab = QWidget()
        root = QVBoxLayout(self.period_tab)

        row = QHBoxLayout()
        row.addWidget(QLabel("Du"))
        self.period_start_edit = QDateEdit(QDate.currentDate().addDays(-30))
        self.period_start_edit.setCalendarPopup(True)
        row.addWidget(self.period_start_edit)
        row.addWidget(QLabel("au"))
        self.period_end_edit = QDateEdit(QDate.currentDate())
        self.period_end_edit.setCalendarPopup(True)
        row.addWidget(self.period_end_edit)

        calc_btn = QPushButton("Calculer")
        calc_btn.clicked.connect(self.refresh_period)
        row.addWidget(calc_btn)

        export_btn = QPushButton("Exporter PDF")
        export_btn.clicked.connect(self.export_period_pdf)
        row.addWidget(export_btn)
        row.addStretch()
        root.addLayout(row)

        summary = QHBoxLayout()
        self.period_expected_label = QLabel("Attendu période: 0.00")
        self.period_actual_label = QLabel("Réel période: 0.00")
        self.period_diff_label = QLabel("Ecart: 0.00")
        summary.addWidget(self.period_expected_label)
        summary.addWidget(self.period_actual_label)
        summary.addWidget(self.period_diff_label)
        summary.addStretch()
        root.addLayout(summary)

        self.period_table = QTableWidget(0, 6)
        self.period_table.setHorizontalHeaderLabels([
            "Date", "Attendu", "Réel", "Ecart", "Validé", "Utilisateur"
        ])
        self._decorate_table(self.period_table)
        root.addWidget(self.period_table)

        self.tabs.addTab(self.period_tab, "Caisse par période")

    def _build_cnam_tab(self):
        self.cnam_tab = QWidget()
        root = QVBoxLayout(self.cnam_tab)

        row = QHBoxLayout()
        row.addWidget(QLabel("Du"))
        self.cnam_start_edit = QDateEdit(QDate.currentDate().addDays(-30))
        self.cnam_start_edit.setCalendarPopup(True)
        row.addWidget(self.cnam_start_edit)
        row.addWidget(QLabel("au"))
        self.cnam_end_edit = QDateEdit(QDate.currentDate())
        self.cnam_end_edit.setCalendarPopup(True)
        row.addWidget(self.cnam_end_edit)

        generate_btn = QPushButton("Générer la liste")
        generate_btn.clicked.connect(self.refresh_cnam)
        row.addWidget(generate_btn)

        export_btn = QPushButton("Exporter PDF")
        export_btn.clicked.connect(self.export_cnam_pdf)
        row.addWidget(export_btn)
        row.addStretch()
        root.addLayout(row)

        self.cnam_total_label = QLabel("Montant total à réclamer CNAM: 0.00")
        root.addWidget(self.cnam_total_label)

        self.cnam_table = QTableWidget(0, 5)
        self.cnam_table.setHorizontalHeaderLabels([
            "Patient", "Couverture", "Nb séances", "Montant CNAM à réclamer", "Période"
        ])
        self._decorate_table(self.cnam_table)
        root.addWidget(self.cnam_table)

        self.tabs.addTab(self.cnam_tab, "Recouvrement CNAM")

    def _db(self):
        return sqlite3.connect(self.db_path)

    def _compute_expected_for_range(self, start_date_text, end_date_text):
        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT IFNULL(SUM(a.paid_amount - IFNULL(au.amount_used, 0)), 0)
            FROM appointments a
            LEFT JOIN advance_usage au ON au.appointment_id = a.id
            WHERE date(a.start_datetime) BETWEEN ? AND ?
              AND a.status IN ('present', 'effectue')
            """,
            (start_date_text, end_date_text),
        )
        sessions_cash = float(cur.fetchone()[0] or 0)

        cur.execute(
            """
            SELECT IFNULL(SUM(amount), 0)
            FROM advance_transactions
            WHERE date(transaction_date) BETWEEN ? AND ?
            """,
            (start_date_text, end_date_text),
        )
        advances_cash = float(cur.fetchone()[0] or 0)
        conn.close()
        return sessions_cash + advances_cash

    def _daily_cash_rows(self, start_date_text, end_date_text):
        conn = self._db()
        cur = conn.cursor()

        cur.execute(
            """
            SELECT date(a.start_datetime), IFNULL(SUM(a.paid_amount - IFNULL(au.amount_used, 0)), 0)
            FROM appointments a
            LEFT JOIN advance_usage au ON au.appointment_id = a.id
            WHERE date(a.start_datetime) BETWEEN ? AND ?
              AND a.status IN ('present', 'effectue')
            GROUP BY date(a.start_datetime)
            """,
            (start_date_text, end_date_text),
        )
        sessions_by_day = {d: float(v or 0) for d, v in cur.fetchall()}

        cur.execute(
            """
            SELECT date(transaction_date), IFNULL(SUM(amount), 0)
            FROM advance_transactions
            WHERE date(transaction_date) BETWEEN ? AND ?
            GROUP BY date(transaction_date)
            """,
            (start_date_text, end_date_text),
        )
        advances_by_day = {d: float(v or 0) for d, v in cur.fetchall()}

        cur.execute(
            """
            SELECT date_jour, IFNULL(actual_amount, 0), IFNULL(validated, 0), IFNULL(validated_by, '')
            FROM cash_closings
            WHERE date_jour BETWEEN ? AND ?
            """,
            (start_date_text, end_date_text),
        )
        closings = {d: (float(a or 0), int(v or 0), u) for d, a, v, u in cur.fetchall()}
        conn.close()

        all_days = sorted(set(sessions_by_day) | set(advances_by_day) | set(closings), reverse=True)
        rows = []
        for day in all_days:
            expected = sessions_by_day.get(day, 0.0) + advances_by_day.get(day, 0.0)
            actual, validated, user = closings.get(day, (0.0, 0, ""))
            diff = actual - expected
            valid_txt = "Oui" if validated == 1 else ("Non" if day in closings else "-")
            rows.append((day, expected, actual, diff, valid_txt, user))
        return rows

    def _populate_cash_table(self, table, rows):
        table.setRowCount(len(rows))
        for r, row in enumerate(rows):
            for c, value in enumerate(row):
                if c in (1, 2, 3):
                    cell = QTableWidgetItem(f"{float(value or 0):.2f}")
                else:
                    cell = QTableWidgetItem(str(value))
                table.setItem(r, c, cell)

    def update_expected_amount(self):
        selected_date = self.daily_date_edit.date().toString("yyyy-MM-dd")
        expected = self._compute_expected_for_range(selected_date, selected_date)
        self.daily_expected_label.setText(f"Montant attendu: {expected:.2f}")

    def validate_cash(self):
        selected_date = self.daily_date_edit.date().toString("yyyy-MM-dd")
        expected = self._compute_expected_for_range(selected_date, selected_date)
        actual = float(self.daily_actual_amount.value())

        conn = self._db()
        cur = conn.cursor()

        cur.execute(
            """
            INSERT INTO cash_closings(date_jour, expected_amount, actual_amount, validated, validated_by)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(date_jour) DO UPDATE SET
                expected_amount=excluded.expected_amount,
                actual_amount=excluded.actual_amount,
                validated=excluded.validated,
                validated_by=excluded.validated_by
            """,
            (
                selected_date,
                expected,
                actual,
                1 if abs(expected - actual) < 0.01 else 0,
                self.utilisateur,
            ),
        )

        conn.commit()
        conn.close()

        if abs(expected - actual) < 0.01:
            QMessageBox.information(self, "Validation", "Caisse validée: montant adéquat.")
        else:
            QMessageBox.warning(self, "Validation", "Ecart détecté entre montant attendu et réel.")

        self.refresh()

    def _month_bounds(self):
        selected = self.month_date_edit.date()
        first_day = QDate(selected.year(), selected.month(), 1)
        last_day = first_day.addMonths(1).addDays(-1)
        return first_day.toString("yyyy-MM-dd"), last_day.toString("yyyy-MM-dd")

    def refresh_monthly(self):
        start_date, end_date = self._month_bounds()
        rows = self._daily_cash_rows(start_date, end_date)
        self._populate_cash_table(self.monthly_table, rows)

        expected = sum(float(r[1]) for r in rows)
        actual = sum(float(r[2]) for r in rows)
        diff = actual - expected
        self.month_expected_label.setText(f"Attendu mensuel: {expected:.2f}")
        self.month_actual_label.setText(f"Réel mensuel: {actual:.2f}")
        self.month_diff_label.setText(f"Ecart: {diff:.2f}")

    def refresh_period(self):
        start_date = self.period_start_edit.date().toString("yyyy-MM-dd")
        end_date = self.period_end_edit.date().toString("yyyy-MM-dd")
        if start_date > end_date:
            QMessageBox.warning(self, "Période", "La date de début doit être antérieure à la date de fin.")
            return

        rows = self._daily_cash_rows(start_date, end_date)
        self._populate_cash_table(self.period_table, rows)

        expected = sum(float(r[1]) for r in rows)
        actual = sum(float(r[2]) for r in rows)
        diff = actual - expected
        self.period_expected_label.setText(f"Attendu période: {expected:.2f}")
        self.period_actual_label.setText(f"Réel période: {actual:.2f}")
        self.period_diff_label.setText(f"Ecart: {diff:.2f}")

    def refresh_cnam(self):
        start_date = self.cnam_start_edit.date().toString("yyyy-MM-dd")
        end_date = self.cnam_end_edit.date().toString("yyyy-MM-dd")
        if start_date > end_date:
            QMessageBox.warning(self, "Recouvrement CNAM", "La date de début doit être antérieure à la date de fin.")
            return

        conn = self._db()
        cur = conn.cursor()
        cur.execute(
            """
            SELECT
                p.nom || ' ' || IFNULL(p.prenom, '') AS patient,
                IFNULL(p.couverture, '') AS couverture,
                COUNT(a.id) AS nb_seances,
                IFNULL(SUM(a.cnam_covered), 0) AS montant_cnam
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            WHERE date(a.start_datetime) BETWEEN ? AND ?
              AND a.status IN ('present', 'effectue')
              AND IFNULL(a.cnam_covered, 0) > 0
            GROUP BY p.id
            ORDER BY montant_cnam DESC
            """,
            (start_date, end_date),
        )
        rows = cur.fetchall()
        conn.close()

        self.cnam_table.setRowCount(len(rows))
        total = 0.0
        period_txt = f"{start_date} -> {end_date}"
        for r, row in enumerate(rows):
            total += float(row[3] or 0)
            values = [row[0], row[1], row[2], f"{float(row[3] or 0):.2f}", period_txt]
            for c, value in enumerate(values):
                self.cnam_table.setItem(r, c, QTableWidgetItem(str(value)))

        self.cnam_total_label.setText(f"Montant total à réclamer CNAM: {total:.2f}")

    def refresh(self):
        self.update_expected_amount()
        end = self.daily_date_edit.date().toString("yyyy-MM-dd")
        start = self.daily_date_edit.date().addDays(-59).toString("yyyy-MM-dd")
        daily_rows = self._daily_cash_rows(start, end)
        self._populate_cash_table(self.daily_table, daily_rows)
        self.refresh_monthly()
        self.refresh_period()
        self.refresh_cnam()

    def _export_table_pdf(self, title, table, default_name):
        path, _ = QFileDialog.getSaveFileName(
            self,
            "Exporter caisse",
            default_name,
            "PDF Files (*.pdf)",
        )
        if not path:
            return

        headers = [
            table.horizontalHeaderItem(i).text()
            for i in range(table.columnCount())
            if not table.isColumnHidden(i)
        ]
        rows = []
        for row in range(table.rowCount()):
            values = []
            for col in range(table.columnCount()):
                if table.isColumnHidden(col):
                    continue
                item = table.item(row, col)
                values.append(item.text() if item else "")
            rows.append(values)

        try:
            export_simple_table_pdf(path, title, headers, rows)
            QMessageBox.information(self, "Export", "PDF généré avec succès.")
        except Exception as exc:
            QMessageBox.critical(self, "Export", str(exc))

    def export_daily_pdf(self):
        selected = self.daily_date_edit.date().toString("yyyy-MM-dd")
        self._export_table_pdf("Journal de caisse", self.daily_table, f"caisse_journaliere_{selected}.pdf")

    def export_monthly_pdf(self):
        selected = self.month_date_edit.date().toString("yyyy_MM")
        self._export_table_pdf("Caisse mensuelle", self.monthly_table, f"caisse_mensuelle_{selected}.pdf")

    def export_period_pdf(self):
        start_date = self.period_start_edit.date().toString("yyyy-MM-dd")
        end_date = self.period_end_edit.date().toString("yyyy-MM-dd")
        self._export_table_pdf("Caisse par période", self.period_table, f"caisse_periode_{start_date}_{end_date}.pdf")

    def export_cnam_pdf(self):
        start_date = self.cnam_start_edit.date().toString("yyyy-MM-dd")
        end_date = self.cnam_end_edit.date().toString("yyyy-MM-dd")
        self._export_table_pdf(
            "Liste des montants à réclamer CNAM",
            self.cnam_table,
            f"recouvrement_cnam_{start_date}_{end_date}.pdf",
        )
