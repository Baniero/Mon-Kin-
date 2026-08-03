import os
import sqlite3
import hashlib
import hmac
import binascii
import sys
import shutil
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent


def _runtime_app_dir():
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return BASE_DIR


DEFAULT_DB = _runtime_app_dir() / "mon_kine_data.db"

ONGLETS = [
    ("Patients", "patient.png", "patients"),
    ("Rendez-vous", "agenda.png", "rendezvous"),
    ("Caisse", "caisse.png", "caisse"),
    ("Statistiques", "stats.png", "statistiques"),
    ("Portail patient", "patient.png", "portail_patient"),
    ("Paramètres", "settings.png", "parametres"),
    ("Gestion utilisateurs", "users.png", "utilisateurs"),
]


def chemin_relatif(*parts):
    return str(BASE_DIR.joinpath(*parts))


def get_db_path():
    env_db = os.environ.get("MON_KINE_DB")
    if env_db:
        return env_db

    db_path = DEFAULT_DB

    # In onefile mode, optionally seed the DB next to the exe from the bundled payload.
    if getattr(sys, "frozen", False) and not db_path.exists():
        meipass = getattr(sys, "_MEIPASS", "")
        if meipass:
            bundled_db = Path(meipass) / "mon_kine_data.db"
            if bundled_db.exists():
                try:
                    shutil.copy2(bundled_db, db_path)
                except Exception:
                    pass

    return str(db_path)


def _table_exists(conn, table_name):
    cur = conn.cursor()
    cur.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name=?",
        (table_name,),
    )
    return cur.fetchone() is not None


def _column_exists(conn, table_name, column_name):
    cur = conn.cursor()
    cur.execute(f"PRAGMA table_info({table_name})")
    cols = [row[1] for row in cur.fetchall()]
    return column_name in cols


def hash_password(password, iterations=260000):
    salt = os.urandom(16)
    digest = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, iterations)
    return f"pbkdf2_sha256${iterations}${binascii.hexlify(salt).decode()}${binascii.hexlify(digest).decode()}"


def verify_password(password, encoded):
    try:
        algo, iteration_text, salt_hex, hash_hex = encoded.split("$", 3)
        if algo != "pbkdf2_sha256":
            return False
        iterations = int(iteration_text)
        salt = binascii.unhexlify(salt_hex.encode())
        expected = binascii.unhexlify(hash_hex.encode())
        current = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, iterations)
        return hmac.compare_digest(current, expected)
    except Exception:
        return False


def init_db(db_path=None):
    db_file = db_path or get_db_path()
    conn = sqlite3.connect(db_file)
    conn.execute("PRAGMA foreign_keys = ON")
    cur = conn.cursor()

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS patients (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            code_patient TEXT UNIQUE,
            dossier_patient TEXT,
            nom TEXT NOT NULL,
            prenom TEXT,
            age INTEGER,
            date_naissance TEXT,
            sexe TEXT,
            telephone1 TEXT,
            telephone2 TEXT,
            adresse TEXT,
            couverture TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS medical_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            diagnostic TEXT,
            medecin_traitant TEXT,
            nb_seances_programme INTEGER DEFAULT 0,
            duree_seance_minutes INTEGER DEFAULT 30,
            nature_seances TEXT,
            objectifs TEXT,
            remarques TEXT,
            updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS session_types (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            libelle TEXT UNIQUE NOT NULL
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS patient_programs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            titre TEXT,
            nature_seances TEXT,
            nb_seances INTEGER DEFAULT 0,
            duree_seance_minutes INTEGER DEFAULT 30,
            date_debut TEXT,
            statut TEXT DEFAULT 'planifie',
            session_price REAL DEFAULT 0,
            patient_share REAL DEFAULT 0,
            cnam_share REAL DEFAULT 0,
            objectifs TEXT,
            remarques TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS patient_timeline (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            event_type TEXT NOT NULL,
            event_date TEXT NOT NULL,
            title TEXT,
            details TEXT,
            created_by TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS patient_attachments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            category TEXT,
            file_path TEXT NOT NULL,
            note TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS patient_alerts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            alert_type TEXT NOT NULL,
            severity TEXT DEFAULT 'moyen',
            content TEXT NOT NULL,
            active INTEGER DEFAULT 1,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT UNIQUE NOT NULL,
            password TEXT NOT NULL,
            password_hash TEXT,
            role TEXT NOT NULL,
            full_name TEXT,
            active INTEGER DEFAULT 1,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS appointments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            kine_id INTEGER,
            start_datetime TEXT NOT NULL,
            end_datetime TEXT,
            acte TEXT,
            room TEXT,
            status TEXT DEFAULT 'planifie',
            payment_status TEXT DEFAULT 'non_paye',
            amount REAL DEFAULT 0,
            paid_amount REAL DEFAULT 0,
            cnam_covered REAL DEFAULT 0,
            notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE,
            FOREIGN KEY(kine_id) REFERENCES users(id) ON DELETE SET NULL
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS cash_closings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date_jour TEXT UNIQUE NOT NULL,
            expected_amount REAL DEFAULT 0,
            actual_amount REAL DEFAULT 0,
            validated INTEGER DEFAULT 0,
            note TEXT,
            validated_by TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS access_permissions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL,
            section_key TEXT NOT NULL,
            allowed INTEGER DEFAULT 1,
            UNIQUE(username, section_key)
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS patient_finance (
            patient_id INTEGER PRIMARY KEY,
            session_price REAL DEFAULT 0,
            patient_share REAL DEFAULT 0,
            cnam_share REAL DEFAULT 0,
            advance_balance REAL DEFAULT 0,
            total_advance_paid REAL DEFAULT 0,
            updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS advance_transactions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            amount REAL NOT NULL,
            transaction_date TEXT DEFAULT CURRENT_TIMESTAMP,
            note TEXT,
            created_by TEXT,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS advance_usage (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            appointment_id INTEGER UNIQUE NOT NULL,
            patient_id INTEGER NOT NULL,
            amount_used REAL NOT NULL,
            used_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(appointment_id) REFERENCES appointments(id) ON DELETE CASCADE,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS finance_ledger (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            appointment_id INTEGER,
            entry_type TEXT NOT NULL,
            amount REAL NOT NULL,
            reference TEXT,
            note TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE,
            FOREIGN KEY(appointment_id) REFERENCES appointments(id) ON DELETE SET NULL
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS payment_audit (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            appointment_id INTEGER NOT NULL,
            patient_id INTEGER NOT NULL,
            old_paid REAL DEFAULT 0,
            new_paid REAL DEFAULT 0,
            old_status TEXT,
            new_status TEXT,
            reason TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(appointment_id) REFERENCES appointments(id) ON DELETE CASCADE,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS advance_lots (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            transaction_id INTEGER,
            original_amount REAL NOT NULL,
            remaining_amount REAL NOT NULL,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE,
            FOREIGN KEY(transaction_id) REFERENCES advance_transactions(id) ON DELETE SET NULL
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS advance_lot_usage (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            lot_id INTEGER NOT NULL,
            appointment_id INTEGER NOT NULL,
            amount_used REAL NOT NULL,
            used_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(lot_id) REFERENCES advance_lots(id) ON DELETE CASCADE,
            FOREIGN KEY(appointment_id) REFERENCES appointments(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS patient_portal_access (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            login_code TEXT UNIQUE NOT NULL,
            pin_code TEXT NOT NULL,
            active INTEGER DEFAULT 1,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE
        )
        """
    )

    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS patient_questionnaires (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            patient_id INTEGER NOT NULL,
            appointment_id INTEGER,
            douleur INTEGER,
            mobilite INTEGER,
            gene INTEGER,
            commentaire TEXT,
            submitted_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(patient_id) REFERENCES patients(id) ON DELETE CASCADE,
            FOREIGN KEY(appointment_id) REFERENCES appointments(id) ON DELETE SET NULL
        )
        """
    )

    default_types = [
        "Rééducation fonctionnelle",
        "Massage thérapeutique",
        "Drainage lymphatique",
        "Renforcement musculaire",
        "Physiothérapie",
    ]
    for stype in default_types:
        cur.execute("INSERT OR IGNORE INTO session_types(libelle) VALUES (?)", (stype,))

    if not _column_exists(conn, "patients", "date_naissance"):
        cur.execute("ALTER TABLE patients ADD COLUMN date_naissance TEXT")

    if not _column_exists(conn, "patients", "dossier_patient"):
        cur.execute("ALTER TABLE patients ADD COLUMN dossier_patient TEXT")

    if not _column_exists(conn, "medical_records", "duree_seance_minutes"):
        cur.execute("ALTER TABLE medical_records ADD COLUMN duree_seance_minutes INTEGER DEFAULT 30")

    if not _column_exists(conn, "patient_programs", "statut"):
        cur.execute("ALTER TABLE patient_programs ADD COLUMN statut TEXT DEFAULT 'planifie'")

    if not _column_exists(conn, "patient_programs", "session_price"):
        cur.execute("ALTER TABLE patient_programs ADD COLUMN session_price REAL DEFAULT 0")
    if not _column_exists(conn, "patient_programs", "patient_share"):
        cur.execute("ALTER TABLE patient_programs ADD COLUMN patient_share REAL DEFAULT 0")
    if not _column_exists(conn, "patient_programs", "cnam_share"):
        cur.execute("ALTER TABLE patient_programs ADD COLUMN cnam_share REAL DEFAULT 0")

    if not _column_exists(conn, "appointments", "room"):
        cur.execute("ALTER TABLE appointments ADD COLUMN room TEXT")

    if not _column_exists(conn, "patient_finance", "patient_share"):
        cur.execute("ALTER TABLE patient_finance ADD COLUMN patient_share REAL DEFAULT 0")

    if not _column_exists(conn, "patient_finance", "cnam_share"):
        cur.execute("ALTER TABLE patient_finance ADD COLUMN cnam_share REAL DEFAULT 0")

    if not _column_exists(conn, "users", "password_hash"):
        cur.execute("ALTER TABLE users ADD COLUMN password_hash TEXT")

    cur.execute(
        "INSERT OR IGNORE INTO users(username, password, password_hash, role, full_name) VALUES (?, ?, ?, ?, ?)",
        ("admin", "legacy", hash_password("admin"), "admin", "Administrateur"),
    )

    # Migrate legacy plain-text passwords to PBKDF2 hash.
    cur.execute(
        "SELECT id, password, IFNULL(password_hash, '') FROM users"
    )
    for user_id, raw_password, pwd_hash in cur.fetchall():
        if pwd_hash:
            continue
        if raw_password:
            cur.execute(
                "UPDATE users SET password_hash=? WHERE id=?",
                (hash_password(raw_password), user_id),
            )

    cur.execute(
        "INSERT OR IGNORE INTO settings(key, value) VALUES (?, ?)",
        ("cabinet_name", "Le cabinet de kinésithérapie et de rééducation"),
    )

    conn.commit()
    conn.close()


def get_setting(key, default_value=""):
    conn = sqlite3.connect(get_db_path())
    cur = conn.cursor()
    cur.execute("SELECT value FROM settings WHERE key=?", (key,))
    row = cur.fetchone()
    conn.close()
    if row:
        return row[0]
    return default_value


def set_setting(key, value):
    conn = sqlite3.connect(get_db_path())
    cur = conn.cursor()
    cur.execute(
        "INSERT INTO settings(key, value) VALUES(?, ?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
        (key, value),
    )
    conn.commit()
    conn.close()


def authenticate_user(username, password):
    conn = sqlite3.connect(get_db_path())
    cur = conn.cursor()
    cur.execute(
        """
        SELECT id, username, role, IFNULL(full_name, ''), active,
               IFNULL(password_hash, ''), IFNULL(password, '')
        FROM users
        WHERE username=?
        """,
        (username,),
    )
    row = cur.fetchone()
    if not row:
        conn.close()
        return None

    user_id, uname, role, full_name, active, password_hash_value, legacy_password = row
    if int(active) != 1:
        conn.close()
        return None

    ok = False
    if password_hash_value:
        ok = verify_password(password, password_hash_value)
    elif legacy_password:
        ok = (legacy_password == password)
        if ok:
            cur.execute(
                "UPDATE users SET password_hash=? WHERE id=?",
                (hash_password(password), user_id),
            )
            conn.commit()

    conn.close()
    if not ok:
        return None

    return {
        "id": user_id,
        "username": uname,
        "role": role,
        "full_name": full_name,
    }


def upsert_user(username, role, full_name, password=None):
    conn = sqlite3.connect(get_db_path())
    cur = conn.cursor()
    cur.execute("SELECT id FROM users WHERE username=?", (username,))
    row = cur.fetchone()

    if row:
        user_id = row[0]
        if password:
            cur.execute(
                "UPDATE users SET password_hash=?, full_name=?, role=?, active=1 WHERE id=?",
                (hash_password(password), full_name, role, user_id),
            )
        else:
            cur.execute(
                "UPDATE users SET full_name=?, role=?, active=1 WHERE id=?",
                (full_name, role, user_id),
            )
    else:
        if not password:
            conn.close()
            raise ValueError("password_required")
        cur.execute(
            "INSERT INTO users(username, password, password_hash, role, full_name, active) VALUES (?, ?, ?, ?, ?, 1)",
            (username, "legacy", hash_password(password), role, full_name),
        )

    conn.commit()
    conn.close()


def deactivate_user(username):
    conn = sqlite3.connect(get_db_path())
    cur = conn.cursor()
    cur.execute("UPDATE users SET active=0 WHERE username=?", (username,))
    conn.commit()
    conn.close()


def get_user_permissions(username):
    conn = sqlite3.connect(get_db_path())
    cur = conn.cursor()
    cur.execute(
        "SELECT section_key, IFNULL(allowed, 1) FROM access_permissions WHERE username=?",
        (username,),
    )
    rows = cur.fetchall()
    conn.close()
    return {key: int(val or 0) == 1 for key, val in rows}


def set_user_permissions(username, permissions_map):
    conn = sqlite3.connect(get_db_path())
    cur = conn.cursor()
    for section_key, allowed in (permissions_map or {}).items():
        cur.execute(
            """
            INSERT INTO access_permissions(username, section_key, allowed)
            VALUES (?, ?, ?)
            ON CONFLICT(username, section_key) DO UPDATE SET allowed=excluded.allowed
            """,
            (username, section_key, 1 if bool(allowed) else 0),
        )
    conn.commit()
    conn.close()
