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
                numero_assuree TEXT
            )
        ";
        cmd.ExecuteNonQuery();
    }
}
