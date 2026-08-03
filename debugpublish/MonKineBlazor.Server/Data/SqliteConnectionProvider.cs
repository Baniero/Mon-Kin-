using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MonKineBlazor.Server.Data;

public static class DatabaseConnectionProvider
{
    public static NpgsqlConnection CreateConnection(string? connectionString = null)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        connectionString ??= Environment.GetEnvironmentVariable("DATABASE_URL");
        connectionString ??= Environment.GetEnvironmentVariable("DefaultConnection");
        connectionString ??= config["DATABASE_URL"];
        connectionString ??= config["DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Aucune chaîne de connexion PostgreSQL n'a été trouvée. Définissez DATABASE_URL ou DefaultConnection.");
        }

        if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(connectionString);
            var userInfo = uri.UserInfo.Split(':', 2);
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Username = userInfo.Length > 0 ? userInfo[0] : string.Empty,
                Password = userInfo.Length > 1 ? userInfo[1] : string.Empty,
                Database = uri.AbsolutePath.TrimStart('/'),
                SslMode = SslMode.Require,
                TrustServerCertificate = true,
                Pooling = true
            };
            return new NpgsqlConnection(builder.ConnectionString);
        }

        return new NpgsqlConnection(connectionString);
    }
}
