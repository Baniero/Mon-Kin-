import base64
import json
import os
import platform
import sys
import uuid
from datetime import datetime
from pathlib import Path

from PyQt6.QtWidgets import (
    QFileDialog,
    QDialog,
    QDialogButtonBox,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
)

LICENSE_DIR_NAME = "MonKine"
LICENSE_FILE_NAME = "offline_license.json"
PUBLIC_KEY_FILE_NAME = "license_public_key.pem"


def _read_machine_guid_windows():
    try:
        import winreg

        access = winreg.KEY_READ
        try:
            access |= winreg.KEY_WOW64_64KEY
        except AttributeError:
            pass

        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\\Microsoft\\Cryptography", 0, access) as key:
            value, _ = winreg.QueryValueEx(key, "MachineGuid")
            return str(value or "")
    except Exception:
        return ""


def get_machine_fingerprint():
    machine_guid = _read_machine_guid_windows().strip()
    import hashlib

    if machine_guid:
        return hashlib.sha256(machine_guid.encode("utf-8")).hexdigest()

    parts = [
        platform.system(),
        platform.release(),
        platform.machine(),
        platform.node(),
        str(uuid.getnode()),
    ]
    raw = "|".join(parts)
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def _app_base_dir():
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent.parent


def _public_key_path():
    return _app_base_dir() / "assets" / PUBLIC_KEY_FILE_NAME


def _license_root_dir():
    base = os.environ.get("PROGRAMDATA") or os.environ.get("APPDATA") or os.path.expanduser("~")
    return os.path.join(base, LICENSE_DIR_NAME)


def _license_file_path():
    return os.path.join(_license_root_dir(), LICENSE_FILE_NAME)


def _canonical_payload_bytes(payload):
    normalized = json.dumps(payload, ensure_ascii=True, sort_keys=True, separators=(",", ":"))
    return normalized.encode("utf-8")


def _verify_signature(payload, signature_b64):
    try:
        from cryptography.hazmat.primitives import serialization
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
    except Exception:
        return False, "Le package cryptography est manquant."

    key_path = _public_key_path()
    if not key_path.exists():
        return False, f"Cle publique introuvable: {key_path}"

    try:
        public_key = serialization.load_pem_public_key(key_path.read_bytes())
        if not isinstance(public_key, Ed25519PublicKey):
            return False, "La cle publique doit etre une cle Ed25519."
        signature = base64.b64decode(signature_b64.encode("ascii"), validate=True)
        public_key.verify(signature, _canonical_payload_bytes(payload))
        return True, ""
    except Exception:
        return False, "Signature de licence invalide."


def _load_license_data():
    license_path = _license_file_path()
    if not os.path.exists(license_path):
        return None
    try:
        with open(license_path, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except Exception:
        return None


def _save_license_data(payload):
    license_dir = _license_root_dir()
    os.makedirs(license_dir, exist_ok=True)
    with open(_license_file_path(), "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=True, indent=2)


class OfflineActivationDialog(QDialog):
    def __init__(self, fingerprint, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Activation hors ligne")
        self.setMinimumWidth(620)
        self.license_path = ""

        layout = QVBoxLayout(self)

        info = QLabel(
            "Cette installation doit etre activee.\n"
            "Entrez le code client et importez un fichier de licence signe."
        )
        info.setWordWrap(True)
        layout.addWidget(info)

        form = QFormLayout()
        self.machine_code = QLineEdit(fingerprint)
        self.machine_code.setReadOnly(True)
        self.activation_code = QLineEdit()
        self.activation_code.setPlaceholderText("Code client unique")
        self.activation_code.setEchoMode(QLineEdit.EchoMode.Password)
        self.license_file = QLineEdit()
        self.license_file.setPlaceholderText("Chemin du fichier licence (.json)")
        self.license_file.setReadOnly(True)

        machine_row = QHBoxLayout()
        machine_row.addWidget(self.machine_code, 1)
        copy_machine_btn = QPushButton("Copier")
        copy_machine_btn.clicked.connect(self._copy_machine_code)
        machine_row.addWidget(copy_machine_btn)

        browse_row = QHBoxLayout()
        browse_row.addWidget(self.license_file, 1)
        browse_btn = QPushButton("Parcourir")
        browse_btn.clicked.connect(self._pick_license_file)
        browse_row.addWidget(browse_btn)

        form.addRow("Code machine", machine_row)
        form.addRow("Code activation", self.activation_code)
        form.addRow("Fichier licence", browse_row)
        layout.addLayout(form)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def _pick_license_file(self):
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            "Selectionner une licence",
            "",
            "Licence JSON (*.json);;Tous les fichiers (*.*)",
        )
        if file_path:
            self.license_path = file_path
            self.license_file.setText(file_path)

    def _copy_machine_code(self):
        from PyQt6.QtWidgets import QApplication

        app = QApplication.instance()
        if app is not None:
            app.clipboard().setText(self.machine_code.text().strip())

    def entered_activation_code(self):
        return self.activation_code.text().strip()

    def entered_license_path(self):
        return (self.license_path or self.license_file.text() or "").strip()


def _validate_license_blob(license_blob, expected_activation_code=None):
    if not isinstance(license_blob, dict):
        return False, "Format de licence invalide."

    payload = license_blob.get("payload")
    signature = license_blob.get("signature")
    if not isinstance(payload, dict) or not isinstance(signature, str):
        return False, "La licence doit contenir payload + signature."

    ok, message = _verify_signature(payload, signature)
    if not ok:
        return False, message

    local_fp = get_machine_fingerprint().strip().lower()
    machine_fp = str(payload.get("machine_fingerprint") or "").strip().lower()
    if machine_fp != local_fp:
        return False, "Cette licence appartient a un autre PC. Utilisez le code machine complet (64 caracteres)."

    activation_code = str(payload.get("activation_code") or "")
    if expected_activation_code and activation_code != expected_activation_code:
        return False, "Le code activation ne correspond pas a la licence."

    expires_at = payload.get("expires_at")
    if expires_at:
        try:
            expiry = datetime.fromisoformat(str(expires_at).replace("Z", "+00:00"))
            now = datetime.now(expiry.tzinfo) if expiry.tzinfo else datetime.utcnow()
            if now > expiry:
                return False, "La licence est expiree."
        except Exception:
            return False, "Date d'expiration invalide dans la licence."

    return True, ""


def is_installation_activated():
    data = _load_license_data()
    if not data:
        return False
    ok, _message = _validate_license_blob(data)
    return ok


def _read_license_file(file_path):
    try:
        with open(file_path, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except Exception:
        return None


def activate_offline_with_license_file(activation_code, license_path):
    if not activation_code:
        return False, "Code activation obligatoire."
    if not license_path or not os.path.exists(license_path):
        return False, "Fichier licence introuvable."

    blob = _read_license_file(license_path)
    if blob is None:
        return False, "Lecture licence impossible."

    ok, message = _validate_license_blob(blob, expected_activation_code=activation_code)
    if not ok:
        return False, message

    _save_license_data(blob)
    return True, ""


def ensure_offline_activation(parent=None):
    if is_installation_activated():
        return True

    fingerprint = get_machine_fingerprint()
    dialog = OfflineActivationDialog(fingerprint=fingerprint, parent=parent)
    while True:
        result = dialog.exec()
        if result != QDialog.DialogCode.Accepted:
            return False

        ok, message = activate_offline_with_license_file(
            dialog.entered_activation_code(),
            dialog.entered_license_path(),
        )
        if ok:
            QMessageBox.information(dialog, "Activation", "Activation reussie pour ce poste.")
            return True

        QMessageBox.warning(dialog, "Activation", message or "Activation impossible. Veuillez reessayer.")
        dialog.activation_code.clear()
