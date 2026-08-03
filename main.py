import sys
from PyQt6.QtWidgets import QApplication, QDialog

from db import init_db
from ui.main_window import MainWindow
from modules.login_dialog import LoginDialog
from modules.offline_activation import ensure_offline_activation


def main():
    init_db()
    app = QApplication(sys.argv)

    if not ensure_offline_activation():
        sys.exit(0)

    login = LoginDialog()
    if login.exec() != QDialog.DialogCode.Accepted or not login.user:
        sys.exit(0)

    username = login.user["username"]
    full_name = login.user.get("full_name", "")
    role = login.user["role"]
    window = MainWindow(utilisateur=username, role=role, nom_complet=full_name)
    window.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
