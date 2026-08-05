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

        EnsureAdminUser(connection);
        EnsureCabinetInfoTable(connection);
        EnsureColumnExists(connection, "cnam_bordereau_executed", "facture_number", "TEXT");
    }

    private static void EnsureAdminUser(NpgsqlConnection connection)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = @username";
        checkCmd.Parameters.AddWithValue("@username", "admin");
        var existingAdmin = Convert.ToInt32(checkCmd.ExecuteScalar());
        if (existingAdmin > 0)
        {
            return;
        }

        var passwordHash = PasswordHasher.Hash("Admin123!");
        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO users (username, full_name, role, active, password_hash)
            VALUES (@username, @full_name, @role, @active, @password_hash)
        ";
        insertCmd.Parameters.AddWithValue("@username", "admin");
        insertCmd.Parameters.AddWithValue("@full_name", "Administrateur");
        insertCmd.Parameters.AddWithValue("@role", "admin");
        insertCmd.Parameters.AddWithValue("@active", true);
        insertCmd.Parameters.AddWithValue("@password_hash", passwordHash);
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
