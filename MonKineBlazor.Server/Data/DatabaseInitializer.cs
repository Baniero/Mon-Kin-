using System.IO;
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
    }
}
