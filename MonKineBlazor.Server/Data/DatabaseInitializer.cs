using System.IO;
using MonKineBlazor.Server.Services;
using Npgsql;

namespace MonKineBlazor.Server.Data;

public static class DatabaseInitializer
{
    public static void EnsureDatabaseCreated(string contentRootPath)
    {
        var scriptPath = Path.Combine(contentRootPath, "postgres-init.sql");
        if (!File.Exists(scriptPath))
        {
            return;
        }

        using var connection = DatabaseConnectionProvider.CreateConnection();
        connection.Open();

        var script = File.ReadAllText(scriptPath);
        using var command = new NpgsqlCommand(script, connection);
        command.ExecuteNonQuery();

        EnsureCabinetsTable(connection);
        EnsureColumnExists(connection, "patient_programs", "prix_seance_ttc", "NUMERIC(18,3) DEFAULT 0");
        EnsureUsersTable(connection);
        var adminCabinetId = EnsureCabinetExists(connection, "CabinetAdmin");
        EnsureAdminUser(connection, adminCabinetId);
        EnsureCabinetInfoTable(connection);
        EnsureColumnExists(connection, "cnam_bordereau_executed", "facture_number", "TEXT");
        EnsureColumnExists(connection, "cnam_bordereau_executed", "encaisse", "BOOLEAN DEFAULT FALSE");
        EnsurePatientsCnamUniqueIndex(connection);
    }

    private static void EnsureCabinetsTable(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
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
                prix_seance_ttc NUMERIC(18,3) DEFAULT 0,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            )
        ";
        cmd.ExecuteNonQuery();

        EnsureColumnExists(connection, "cabinets", "code_etablissement", "TEXT");
        EnsureColumnExists(connection, "cabinets", "matricule_fiscal", "TEXT");
        EnsureColumnExists(connection, "cabinets", "nom_etablissement", "TEXT");
        EnsureColumnExists(connection, "cabinets", "racine", "TEXT");
        EnsureColumnExists(connection, "cabinets", "cle", "TEXT");
        EnsureColumnExists(connection, "cabinets", "qualite", "TEXT");
        EnsureColumnExists(connection, "cabinets", "adresse_cabinet", "TEXT");
        EnsureColumnExists(connection, "cabinets", "nom_cabinet_arabe", "TEXT");
        EnsureColumnExists(connection, "cabinets", "nom_kine_arabe", "TEXT");
        EnsureColumnExists(connection, "cabinets", "adresse_kine_arabe", "TEXT");
        EnsureColumnExists(connection, "cabinets", "numero_assuree", "TEXT");
        EnsureColumnExists(connection, "cabinets", "programme_type_options", "TEXT");
        EnsureColumnExists(connection, "cabinets", "nature_seances_options", "TEXT");
        EnsureColumnExists(connection, "cabinets", "prix_seance_ttc", "NUMERIC(18,3) DEFAULT 0");
    }

    private static int EnsureCabinetExists(NpgsqlConnection connection, string cabinetName)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = @"
            SELECT id
            FROM cabinets
            WHERE nom_cabinet = @nomCabinet
            LIMIT 1
        ";
        checkCmd.Parameters.AddWithValue("@nomCabinet", cabinetName);

        var existingId = checkCmd.ExecuteScalar();
        if (existingId != null && existingId != DBNull.Value)
        {
            return Convert.ToInt32(existingId);
        }

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO cabinets (nom_cabinet, created_at)
            VALUES (@nomCabinet, NOW())
            RETURNING id
        ";
        insertCmd.Parameters.AddWithValue("@nomCabinet", cabinetName);

        return Convert.ToInt32(insertCmd.ExecuteScalar());
    }

    private static void EnsureUsersTable(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id SERIAL PRIMARY KEY,
                username TEXT UNIQUE NOT NULL,
                full_name TEXT,
                role TEXT,
                active BOOLEAN DEFAULT TRUE,
                password_hash TEXT,
                cabinet_id INTEGER REFERENCES cabinets(id),
                allowed_modules TEXT,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            )
        ";
        cmd.ExecuteNonQuery();

        EnsureColumnExists(connection, "users", "password_hash", "TEXT");
        EnsureColumnExists(connection, "users", "cabinet_id", "INTEGER REFERENCES cabinets(id)");
        EnsureColumnExists(connection, "users", "allowed_modules", "TEXT");
    }

    private static void EnsureAdminUser(NpgsqlConnection connection, int adminCabinetId)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = @username";
        checkCmd.Parameters.AddWithValue("@username", "admin");
        var existingAdmin = Convert.ToInt32(checkCmd.ExecuteScalar());
        if (existingAdmin > 0)
        {
            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = @"
                UPDATE users
                SET full_name = @full_name,
                    role = @role,
                    active = @active,
                    password_hash = @password_hash,
                    cabinet_id = @cabinet_id
                WHERE username = @username
            ";
            updateCmd.Parameters.AddWithValue("@full_name", "Administrateur");
            updateCmd.Parameters.AddWithValue("@role", "admin");
            updateCmd.Parameters.AddWithValue("@active", true);
            updateCmd.Parameters.AddWithValue("@password_hash", PasswordHasher.Hash("Admin123!"));
            updateCmd.Parameters.AddWithValue("@cabinet_id", adminCabinetId);
            updateCmd.Parameters.AddWithValue("@username", "admin");
            updateCmd.ExecuteNonQuery();
            return;
        }

        var passwordHash = PasswordHasher.Hash("Admin123!");
        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO users (username, full_name, role, active, password_hash, cabinet_id)
            VALUES (@username, @full_name, @role, @active, @password_hash, @cabinet_id)
        ";
        insertCmd.Parameters.AddWithValue("@username", "admin");
        insertCmd.Parameters.AddWithValue("@full_name", "Administrateur");
        insertCmd.Parameters.AddWithValue("@role", "admin");
        insertCmd.Parameters.AddWithValue("@active", true);
        insertCmd.Parameters.AddWithValue("@password_hash", passwordHash);
        insertCmd.Parameters.AddWithValue("@cabinet_id", adminCabinetId);
        insertCmd.ExecuteNonQuery();
    }

    private static void EnsureCabinetInfoTable(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS cabinet_info (
                id SERIAL PRIMARY KEY,
                nom_cabinet TEXT,
                racine TEXT,
                cle TEXT,
                qualite TEXT,
                numero_assuree TEXT,
                code_etablissement TEXT,
                matricule_fiscal TEXT,
                nom_etablissement TEXT,
                adresse_cabinet TEXT,
                nom_cabinet_arabe TEXT,
                nom_kine_arabe TEXT,
                adresse_kine_arabe TEXT
            )
        ";
        cmd.ExecuteNonQuery();

        EnsureColumnExists(connection, "cabinet_info", "code_etablissement", "TEXT");
        EnsureColumnExists(connection, "cabinet_info", "matricule_fiscal", "TEXT");
        EnsureColumnExists(connection, "cabinet_info", "nom_etablissement", "TEXT");
        EnsureColumnExists(connection, "cabinet_info", "adresse_cabinet", "TEXT");
        EnsureColumnExists(connection, "cabinet_info", "nom_cabinet_arabe", "TEXT");
        EnsureColumnExists(connection, "cabinet_info", "nom_kine_arabe", "TEXT");
        EnsureColumnExists(connection, "cabinet_info", "adresse_kine_arabe", "TEXT");
    }

    private static void EnsurePatientsCnamUniqueIndex(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE UNIQUE INDEX IF NOT EXISTS idx_patients_cnam_unique
            ON patients (
                cabinet_id,
                lower(trim(coalesce(racine, ''))),
                lower(trim(coalesce(cle, ''))),
                lower(trim(coalesce(qualite, '')))
            )
        ";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureColumnExists(NpgsqlConnection connection, string tableName, string columnName, string columnType)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = @"
            SELECT 1
            FROM information_schema.columns
            WHERE table_name = @tableName
              AND column_name = @columnName
        ";
        checkCmd.Parameters.AddWithValue("@tableName", tableName);
        checkCmd.Parameters.AddWithValue("@columnName", columnName);

        var exists = checkCmd.ExecuteScalar();
        if (exists == null)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
            alterCmd.ExecuteNonQuery();
        }
    }
}
