-- PostgreSQL initialization script for MonKineBlazor

CREATE TABLE IF NOT EXISTS cabinets (
    id SERIAL PRIMARY KEY,
    nom_cabinet TEXT NOT NULL,
    code_etablissement TEXT,
    matricule_fiscal TEXT,
    nom_etablissement TEXT,
    racine TEXT,
    cle TEXT,
    qualite TEXT,
    adresse_cabinet TEXT,
    nom_cabinet_arabe TEXT,
    nom_kine_arabe TEXT,
    adresse_kine_arabe TEXT,
    numero_assuree TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS patients (
    id SERIAL PRIMARY KEY,
    cabinet_id INTEGER REFERENCES cabinets(id),
    code_patient TEXT UNIQUE,
    dossier_patient TEXT,
    nom TEXT NOT NULL,
    prenom TEXT,
    age INTEGER,
    date_naissance DATE,
    sexe TEXT,
    telephone1 TEXT,
    telephone2 TEXT,
    adresse TEXT,
    couverture TEXT,
    racine TEXT,
    cle TEXT,
    qualite TEXT,
    n_assuree TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
ALTER TABLE patients ADD COLUMN IF NOT EXISTS cabinet_id INTEGER REFERENCES cabinets(id);

ALTER TABLE patients ADD COLUMN IF NOT EXISTS racine TEXT;
ALTER TABLE patients ADD COLUMN IF NOT EXISTS cle TEXT;
ALTER TABLE patients ADD COLUMN IF NOT EXISTS qualite TEXT;
ALTER TABLE patients ADD COLUMN IF NOT EXISTS n_assuree TEXT;

CREATE TABLE IF NOT EXISTS medical_records (
    id SERIAL PRIMARY KEY,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    diagnostic TEXT,
    medecin_traitant TEXT,
    nb_seances_programme INTEGER DEFAULT 0,
    duree_seance_minutes INTEGER DEFAULT 30,
    nature_seances TEXT,
    objectifs TEXT,
    remarques TEXT,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS session_types (
    id SERIAL PRIMARY KEY,
    libelle TEXT UNIQUE NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    username TEXT UNIQUE NOT NULL,
    full_name TEXT,
    role TEXT,
    active BOOLEAN DEFAULT TRUE,
    password_hash TEXT,
    cabinet_id INTEGER REFERENCES cabinets(id),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS cabinet_id INTEGER REFERENCES cabinets(id);

CREATE TABLE IF NOT EXISTS patient_programs (
    id SERIAL PRIMARY KEY,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    titre TEXT,
    nature_seances TEXT,
    nb_seances INTEGER DEFAULT 0,
    nb_seances_par_semaine INTEGER DEFAULT 1,
    duree_seance_minutes INTEGER DEFAULT 30,
    date_debut DATE,
    date_fin DATE,
    code_bureau TEXT,
    annee TEXT,
    numero_decision TEXT,
    numero_ordre TEXT,
    prix_unitaire NUMERIC DEFAULT 0,
    prix_ht NUMERIC DEFAULT 0,
    tva NUMERIC DEFAULT 0,
    montant_tva NUMERIC DEFAULT 0,
    prix_ttc NUMERIC DEFAULT 0,
    statut TEXT DEFAULT 'planifie',
    objectifs TEXT,
    remarques TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS nb_seances_par_semaine INTEGER DEFAULT 1;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS date_debut DATE;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS date_fin DATE;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS code_bureau TEXT;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS annee TEXT;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS numero_decision TEXT;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS numero_ordre TEXT;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS prix_unitaire NUMERIC DEFAULT 0;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS prix_ht NUMERIC DEFAULT 0;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS tva NUMERIC DEFAULT 0;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS montant_tva NUMERIC DEFAULT 0;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS prix_ttc NUMERIC DEFAULT 0;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS statut TEXT DEFAULT 'planifie';
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS objectifs TEXT;
ALTER TABLE patient_programs ADD COLUMN IF NOT EXISTS remarques TEXT;

CREATE TABLE IF NOT EXISTS patient_finance (
    patient_id INTEGER PRIMARY KEY REFERENCES patients(id) ON DELETE CASCADE,
    session_price NUMERIC DEFAULT 0,
    patient_share NUMERIC DEFAULT 0,
    cnam_share NUMERIC DEFAULT 0,
    advance_balance NUMERIC DEFAULT 0,
    total_advance_paid NUMERIC DEFAULT 0,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS advance_transactions (
    id SERIAL PRIMARY KEY,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    amount NUMERIC NOT NULL,
    transaction_date TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    note TEXT,
    created_by TEXT
);

CREATE TABLE IF NOT EXISTS advance_usage (
    id SERIAL PRIMARY KEY,
    appointment_id INTEGER UNIQUE NOT NULL,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    amount_used NUMERIC NOT NULL,
    used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS advance_lots (
    id SERIAL PRIMARY KEY,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    transaction_id INTEGER NOT NULL REFERENCES advance_transactions(id) ON DELETE CASCADE,
    original_amount NUMERIC NOT NULL,
    remaining_amount NUMERIC NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS advance_lot_usage (
    id SERIAL PRIMARY KEY,
    lot_id INTEGER NOT NULL REFERENCES advance_lots(id) ON DELETE CASCADE,
    appointment_id INTEGER NOT NULL REFERENCES appointments(id) ON DELETE CASCADE,
    amount_used NUMERIC NOT NULL,
    used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS cash_closings (
    date_jour DATE PRIMARY KEY,
    expected_amount NUMERIC DEFAULT 0,
    actual_amount NUMERIC DEFAULT 0,
    validated BOOLEAN DEFAULT FALSE,
    validated_by TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS cnam_bordereau_executed (
    id SERIAL PRIMARY KEY,
    program_id INTEGER NOT NULL UNIQUE REFERENCES patient_programs(id) ON DELETE CASCADE,
    executed_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    executed_by TEXT,
    facture_number TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE cnam_bordereau_executed
    ADD COLUMN IF NOT EXISTS facture_number TEXT;

CREATE TABLE IF NOT EXISTS appointments (
    id SERIAL PRIMARY KEY,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    kine_id INTEGER,
    start_datetime TIMESTAMP WITH TIME ZONE NOT NULL,
    end_datetime TIMESTAMP WITH TIME ZONE,
    acte TEXT,
    room TEXT,
    status TEXT DEFAULT 'planifie',
    payment_status TEXT DEFAULT 'non_paye',
    amount NUMERIC DEFAULT 0,
    paid_amount NUMERIC DEFAULT 0,
    cnam_covered NUMERIC DEFAULT 0,
    notes TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS finance_ledger (
    id SERIAL PRIMARY KEY,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    appointment_id INTEGER,
    entry_type TEXT NOT NULL,
    amount NUMERIC NOT NULL,
    reference TEXT,
    note TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS payment_audit (
    id SERIAL PRIMARY KEY,
    appointment_id INTEGER NOT NULL REFERENCES appointments(id) ON DELETE CASCADE,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    old_paid NUMERIC DEFAULT 0,
    new_paid NUMERIC DEFAULT 0,
    old_status TEXT,
    new_status TEXT,
    reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS patient_portal_access (
    id SERIAL PRIMARY KEY,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    login_code TEXT UNIQUE NOT NULL,
    pin_code TEXT NOT NULL,
    active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS patient_questionnaires (
    id SERIAL PRIMARY KEY,
    patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
    appointment_id INTEGER,
    douleur INTEGER,
    mobilite INTEGER,
    gene INTEGER,
    commentaire TEXT,
    submitted_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_patients_nom_prenom ON patients(nom, prenom);
CREATE INDEX IF NOT EXISTS idx_patient_programs_patient_id ON patient_programs(patient_id);
CREATE INDEX IF NOT EXISTS idx_appointments_patient_id ON appointments(patient_id);
CREATE INDEX IF NOT EXISTS idx_advance_usage_appointment_id ON advance_usage(appointment_id);
