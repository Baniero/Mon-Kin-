using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MonKineBlazor.Server.Data;

public static class DatabaseConnectionProvider
{
    public static NpgsqlConnection CreateConnection(string? connectionString = null)
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            environmentName = "Development";
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
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
                TrustServerCertificate = true,
                Pooling = true
            };

            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            var sslModeValue = query.Select(p => p.Split('=', 2))
                                     .FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[0], "sslmode", StringComparison.OrdinalIgnoreCase))?
                                     [1];

            if (!string.IsNullOrWhiteSpace(sslModeValue))
            {
                builder.SslMode = sslModeValue.ToLowerInvariant() switch
                {
                    "disable" => SslMode.Disable,
                    "allow" => SslMode.Allow,
                    "prefer" => SslMode.Prefer,
                    "require" => SslMode.Require,
                    "verify-ca" => SslMode.VerifyCA,
                    "verify-full" => SslMode.VerifyFull,
                    _ => SslMode.Require
                };
            }
            else if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase))
            {
                builder.SslMode = SslMode.Disable;
            }
            else
            {
                builder.SslMode = SslMode.Require;
            }

            return new NpgsqlConnection(builder.ConnectionString);
        }

        return new NpgsqlConnection(connectionString);
    }
}
