import sys
import os
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
import sqlite3
from PyQt6.QtWidgets import (
    QMainWindow, QTabWidget, QApplication, QMessageBox, QWidget,
    QVBoxLayout, QLabel, QPushButton, QDialog, QListWidget,
    QListWidgetItem, QHBoxLayout, QGraphicsDropShadowEffect, QFrame
)
from PyQt6.QtCore import Qt, QTimer, QEvent, pyqtSignal, QSettings
from PyQt6.QtGui import QIcon, QPixmap

# Imports centralisés
from db import get_db_path, ONGLETS, get_setting, get_user_permissions

from modules.patients_widget import PatientsWidget
from modules.rendezvous_widget import RendezVousWidget
from modules.caisse_widget import CaisseWidget
from modules.users_widget import UsersWidget
from modules.statistiques_widget import StatistiquesWidget
from modules.settings_widget import SettingsWidget
from modules.patient_portal_widget import PatientPortalWidget

DB_FILE = get_db_path()


class NotificationDialog(QDialog):
    def __init__(self, messages, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Notifications")
        self.setMinimumWidth(500)

        layout = QVBoxLayout(self)
        self.list_widget = QListWidget()
        for message in messages:
            self.list_widget.addItem(QListWidgetItem(message))
        layout.addWidget(self.list_widget)

        close_btn = QPushButton("Fermer")
        close_btn.clicked.connect(self.accept)
        layout.addWidget(close_btn)


class MainWindow(QMainWindow):
    auto_logout = pyqtSignal()
    INACTIVITY_TIMEOUT_MS = 3 * 60 * 60 * 1000

    def __init__(self, utilisateur, role, droits=None, db_path=DB_FILE, nom_complet=""):
        super().__init__()
        self.utilisateur = utilisateur
        self.nom_complet = (nom_complet or "").strip()
        self.role = role
        self.droits = droits or []
        self.db_path = db_path
        self.theme_mode = "Clair"
        self.permissions_map = get_user_permissions(self.utilisateur)

        self.setWindowTitle(self._build_title())
        self.resize(1500, 900)
        self.load_settings()

        self.start_inactivity_timer()
        QApplication.instance().installEventFilter(self)

        self._build_ui()
        self.set_theme(self.theme_mode)

    def _build_title(self):
        cabinet_name = get_setting(
            "cabinet_name",
            "Le cabinet de kinésithérapie et de rééducation"
        )
        display_name = self.nom_complet if self.nom_complet else self.utilisateur
        return f"{cabinet_name} - Connecté : {display_name} ({self.role})"

    def _build_cabinet_title(self):
        return get_setting(
            "cabinet_name",
            "Le cabinet de kinésithérapie et de rééducation"
        )

    def _get_home_metrics(self):
        conn = sqlite3.connect(self.db_path)
        cur = conn.cursor()
        cur.execute("SELECT COUNT(*) FROM patients")
        patients = int(cur.fetchone()[0] or 0)
        cur.execute("SELECT COUNT(*) FROM appointments")
        appointments = int(cur.fetchone()[0] or 0)
        cur.execute("SELECT COUNT(*) FROM appointments WHERE payment_status='non_paye'")
        unpaid = int(cur.fetchone()[0] or 0)
        conn.close()
        return {
            "patients": patients,
            "appointments": appointments,
            "unpaid": unpaid,
        }

    def _find_brand_image(self, assets_path):
        preferred_tokens = ("logo", "kine", "icon", "mon_kine", "mon kin")
        image_exts = (".png", ".jpg", ".jpeg", ".webp", ".bmp")
        candidates = []
        try:
            for entry in os.listdir(assets_path):
                lower = entry.lower()
                if lower.endswith(image_exts):
                    candidates.append(entry)
        except Exception:
            return None

        if not candidates:
            return None

        for token in preferred_tokens:
            for entry in candidates:
                if token in entry.lower():
                    return os.path.join(assets_path, entry)

        return os.path.join(assets_path, sorted(candidates)[0])

    def _build_ui(self):
        base_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
        assets_path = os.path.join(base_dir, "assets")
        self.brand_image_path = self._find_brand_image(assets_path)
        if self.brand_image_path and os.path.exists(self.brand_image_path):
            self.setWindowIcon(QIcon(self.brand_image_path))

        self.notification_messages = []

        self.tabs = QTabWidget()
        self.tabs.setTabPosition(QTabWidget.TabPosition.North)
        self.tabs.tabBar().setExpanding(True)
        self.setCentralWidget(self.tabs)

        accueil_widget = self._build_accueil_widget(assets_path)
        self.tabs.addTab(accueil_widget, "Accueil")

        self.patients_widget = PatientsWidget(db_path=self.db_path)
        self.rendezvous_widget = RendezVousWidget(db_path=self.db_path)
        self.caisse_widget = CaisseWidget(db_path=self.db_path, utilisateur=self.utilisateur)
        self.statistiques_widget = StatistiquesWidget(db_path=self.db_path)
        self.patient_portal_widget = PatientPortalWidget(db_path=self.db_path)
        self.settings_widget = SettingsWidget(
            db_path=self.db_path,
            current_theme=self.theme_mode,
            on_theme_change=self.set_theme,
            on_refresh_current_tab=self.refresh_current_tab,
            on_refresh_notifications=self.update_notifications,
            get_notifications=lambda: self.notification_messages,
        )
        self.users_widget = UsersWidget(db_path=self.db_path, on_cabinet_name_changed=self._on_cabinet_name_changed)

        widgets_dict = {
            "Patients": self.patients_widget,
            "Rendez-vous": self.rendezvous_widget,
            "Caisse": self.caisse_widget,
            "Statistiques": self.statistiques_widget,
            "Portail patient": self.patient_portal_widget,
            "Paramètres": self.settings_widget,
            "Gestion utilisateurs": self.users_widget,
        }
        tab_keys = {
            "Patients": "tab.patients",
            "Rendez-vous": "tab.rendezvous",
            "Caisse": "tab.caisse",
            "Statistiques": "tab.statistiques",
            "Portail patient": "tab.portail_patient",
            "Paramètres": "tab.parametres",
            "Gestion utilisateurs": "tab.utilisateurs",
        }

        for nom_onglet, _icon, _attr in ONGLETS:
            widget = widgets_dict.get(nom_onglet)
            if widget is not None and self._is_allowed_tab(tab_keys.get(nom_onglet)):
                self.tabs.addTab(widget, nom_onglet)

        self._apply_subtab_permissions()

        self.notif_timer = QTimer(self)
        self.notif_timer.timeout.connect(self.update_notifications)
        self.notif_timer.start(5 * 60 * 1000)
        self.update_notifications()

    def _build_accueil_widget(self, assets_path):
        accueil_widget = QWidget()
        accueil_widget.setObjectName("welcomeBackground")
        layout = QVBoxLayout(accueil_widget)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(28, 28, 28, 28)

        bg_image_path = self.brand_image_path if self.brand_image_path and os.path.exists(self.brand_image_path) else None
        if bg_image_path:
            bg_url = bg_image_path.replace("\\", "/")
            accueil_widget.setStyleSheet(
                f"""
                QWidget#welcomeBackground {{
                    background-color: qlineargradient(spread:pad, x1:0, y1:0, x2:1, y2:1, stop:0 #eef2ff, stop:1 #f8fafc);
                    background-image: linear-gradient(to bottom right, rgba(236, 239, 255, 0.95), rgba(248, 250, 252, 0.95)), url('{bg_url}');
                    background-position: center;
                    background-repeat: no-repeat;
                    background-attachment: fixed;
                }}
                QWidget#welcomeCard {{
                    background-color: rgba(255, 255, 255, 250);
                    border: 1px solid #e2e8f0;
                    border-radius: 24px;
                }}
                """
            )
        else:
            accueil_widget.setStyleSheet(
                """
                QWidget#welcomeBackground {
                    background-color: qlineargradient(spread:pad, x1:0, y1:0, x2:1, y2:1, stop:0 #eef2ff, stop:1 #f8fafc);
                }
                QWidget#welcomeCard {
                    background-color: rgba(255, 255, 255, 250);
                    border: 1px solid #e2e8f0;
                    border-radius: 24px;
                }
                """
            )

        card_widget = QWidget()
        card_widget.setObjectName("welcomeCard")
        card_widget.setMaximumWidth(980)
        card_layout = QVBoxLayout(card_widget)
        card_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        card_layout.setContentsMargins(30, 28, 30, 28)
        card_layout.setSpacing(14)

        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(28)
        shadow.setColor(Qt.GlobalColor.lightGray)
        shadow.setOffset(0, 8)
        card_widget.setGraphicsEffect(shadow)

        logo_path = self.brand_image_path if self.brand_image_path and os.path.exists(self.brand_image_path) else None
        if logo_path:
            logo = QLabel()
            pix = QPixmap(logo_path)
            logo.setPixmap(pix.scaledToWidth(210, Qt.TransformationMode.SmoothTransformation))
            logo.setAlignment(Qt.AlignmentFlag.AlignCenter)
            card_layout.addWidget(logo)

        title = QLabel(self._build_cabinet_title())
        title.setObjectName("homeTitle")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        card_layout.addWidget(title)

        connected_name = self.nom_complet if self.nom_complet else self.utilisateur
        subtitle = QLabel(f"Vous êtes connecté : {connected_name}")
        subtitle.setObjectName("homeSubtitle")
        subtitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        card_layout.addWidget(subtitle)

        subtitle2 = QLabel("Gestion des patients, des séances, du planning et de la caisse")
        subtitle2.setObjectName("homeSubtitle")
        subtitle2.setAlignment(Qt.AlignmentFlag.AlignCenter)
        card_layout.addWidget(subtitle2)

        metrics = self._get_home_metrics()
        metric_container = QWidget()
        metric_container.setObjectName("homeMetrics")
        metric_layout = QHBoxLayout(metric_container)
        metric_layout.setSpacing(14)
        metric_layout.setContentsMargins(0, 0, 0, 0)

        for label, value in [
            ("Patients enregistrés", metrics["patients"]),
            ("Séances totales", metrics["appointments"]),
            ("Séances non payées", metrics["unpaid"]),
        ]:
            card = QFrame()
            card.setObjectName("homeMetricCard")
            card_layout_inner = QVBoxLayout(card)
            card_layout_inner.setContentsMargins(16, 16, 16, 16)
            card_layout_inner.setSpacing(6)

            value_label = QLabel(str(value))
            value_label.setObjectName("homeMetricValue")
            desc_label = QLabel(label)
            desc_label.setObjectName("homeMetricLabel")
            desc_label.setWordWrap(True)

            card_layout_inner.addWidget(value_label)
            card_layout_inner.addWidget(desc_label)
            metric_layout.addWidget(card)

        card_layout.addWidget(metric_container)

        quick_layout = QHBoxLayout()
        quick_layout.addStretch()
        for tab_name in ["Patients", "Rendez-vous", "Caisse", "Statistiques", "Portail patient", "Paramètres", "Gestion utilisateurs"]:
            btn = QPushButton(tab_name)
            btn.setObjectName("homeNavButton")
            btn.clicked.connect(lambda checked=False, t=tab_name: self.aller_onglet(t))
            quick_layout.addWidget(btn)
        quick_layout.addStretch()
        card_layout.addLayout(quick_layout)

        layout.addWidget(card_widget, alignment=Qt.AlignmentFlag.AlignCenter)

        return accueil_widget

    def _on_cabinet_name_changed(self):
        self.setWindowTitle(self._build_title())

    def start_inactivity_timer(self):
        self.inactivity_timer = QTimer(self)
        self.inactivity_timer.setInterval(self.INACTIVITY_TIMEOUT_MS)
        self.inactivity_timer.timeout.connect(self.handle_inactivity_logout)
        self.inactivity_timer.start()

    def reset_inactivity_timer(self):
        if hasattr(self, "inactivity_timer"):
            self.inactivity_timer.start(self.INACTIVITY_TIMEOUT_MS)

    def handle_inactivity_logout(self):
        QMessageBox.information(self, "Déconnexion automatique", "Vous avez été déconnecté après 3 heures d'inactivité.")
        self.auto_logout.emit()
        self.close()

    def eventFilter(self, obj, event):
        if event.type() in (QEvent.Type.MouseMove, QEvent.Type.MouseButtonPress, QEvent.Type.KeyPress, QEvent.Type.Wheel):
            self.reset_inactivity_timer()
        return super().eventFilter(obj, event)

    def aller_onglet(self, nom_onglet):
        for i in range(self.tabs.count()):
            if self.tabs.tabText(i).lower() == nom_onglet.lower():
                self.tabs.setCurrentIndex(i)
                return

    def _is_allowed_tab(self, tab_key):
        if self.role == "admin":
            return True
        if not tab_key:
            return True
        return self.permissions_map.get(tab_key, True)

    def _apply_subtab_permissions(self):
        if self.role == "admin":
            return
        for widget in [
            getattr(self, "patients_widget", None),
            getattr(self, "rendezvous_widget", None),
            getattr(self, "settings_widget", None),
        ]:
            if widget is not None and hasattr(widget, "apply_permissions"):
                widget.apply_permissions(self.permissions_map)

    def set_theme(self, theme_name):
        base_path = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
        theme_files = {
            "Clair": "style.qss",
            "Sombre": "style_dark.qss",
            "Vert": "style_green.qss",
        }
        qss_file = theme_files.get(theme_name, "style.qss")
        qss_path = os.path.join(base_path, "assets", qss_file)
        if os.path.exists(qss_path):
            with open(qss_path, "r", encoding="utf-8") as f:
                app = QApplication.instance()
                if app is not None:
                    app.setStyleSheet(f.read())
        self.theme_mode = theme_name

    def refresh_current_tab(self):
        widget = self.tabs.currentWidget()
        for method_name in ("refresh", "refresh_data", "reload", "update_table", "actualiser"):
            if hasattr(widget, method_name):
                getattr(widget, method_name)()
                break

    def update_notifications(self):
        self.notification_messages = []
        try:
            conn = sqlite3.connect(self.db_path)
            cur = conn.cursor()
            cur.execute(
                """
                SELECT p.nom, p.prenom, a.start_datetime, a.status
                FROM appointments a
                JOIN patients p ON p.id = a.patient_id
                WHERE date(a.start_datetime) = date('now')
                ORDER BY a.start_datetime
                """
            )
            rows = cur.fetchall()
            for nom, prenom, start_dt, status in rows:
                self.notification_messages.append(
                    f"Aujourd'hui: {nom} {prenom} à {start_dt[11:16]} - statut: {status}"
                )
            conn.close()
        except Exception:
            self.notification_messages = []
        if hasattr(self, "settings_widget"):
            self.settings_widget.reload_notifications()

    def show_notifications(self):
        dialog = NotificationDialog(self.notification_messages, self)
        dialog.exec()

    def load_settings(self):
        settings = QSettings("MonKine", "MainWindow")
        geometry = settings.value("geometry")
        if geometry:
            self.restoreGeometry(geometry)
        state = settings.value("windowState")
        if state:
            self.restoreState(state)

    def save_settings(self):
        settings = QSettings("MonKine", "MainWindow")
        settings.setValue("geometry", self.saveGeometry())
        settings.setValue("windowState", self.saveState())

    def closeEvent(self, event):
        self.save_settings()
        super().closeEvent(event)
