import os

from PyQt6.QtWidgets import (
    QDialog, QVBoxLayout, QFormLayout, QLineEdit, QPushButton, QMessageBox, QLabel
)
from PyQt6.QtGui import QPixmap, QIcon
from PyQt6.QtCore import Qt

from db import authenticate_user


class LoginDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.user = None
        self.setWindowTitle("Connexion - Mon Kine")
        self.setMinimumWidth(360)

        base_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
        assets_path = os.path.join(base_dir, "assets")
        self.brand_image_path = self._find_brand_image(assets_path)

        self._apply_theme(base_dir)
        if self.brand_image_path and os.path.exists(self.brand_image_path):
            self.setWindowIcon(QIcon(self.brand_image_path))

        root = QVBoxLayout(self)

        if self.brand_image_path and os.path.exists(self.brand_image_path):
            logo = QLabel()
            pix = QPixmap(self.brand_image_path)
            logo.setPixmap(pix.scaledToWidth(140, Qt.TransformationMode.SmoothTransformation))
            logo.setAlignment(Qt.AlignmentFlag.AlignCenter)
            root.addWidget(logo)

        title = QLabel("Connexion au cabinet")
        title.setObjectName("sectionTitle")
        root.addWidget(title)

        form = QFormLayout()
        self.username_input = QLineEdit()
        self.username_input.setPlaceholderText("admin")
        self.password_input = QLineEdit()
        self.password_input.setEchoMode(QLineEdit.EchoMode.Password)

        form.addRow("Utilisateur", self.username_input)
        form.addRow("Mot de passe", self.password_input)
        root.addLayout(form)

        login_btn = QPushButton("Se connecter")
        login_btn.clicked.connect(self.try_login)
        root.addWidget(login_btn)

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

    def _apply_theme(self, base_dir):
        qss_path = os.path.join(base_dir, "assets", "style.qss")
        if os.path.exists(qss_path):
            try:
                with open(qss_path, "r", encoding="utf-8") as f:
                    self.setStyleSheet(f.read())
            except Exception:
                pass

    def try_login(self):
        username = self.username_input.text().strip()
        password = self.password_input.text().strip()
        if not username or not password:
            QMessageBox.warning(self, "Validation", "Utilisateur et mot de passe sont obligatoires.")
            return

        user = authenticate_user(username, password)
        if not user:
            QMessageBox.critical(self, "Connexion", "Identifiants invalides ou compte inactif.")
            return

        self.user = user
        self.accept()
