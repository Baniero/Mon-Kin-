using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Shared.Models;
using Npgsql;

namespace MonKineBlazor.Server.Services;

public static class UserContextHelper
{
    private const string UserIdHeader = "X-User-Id";

    public static UserDto? GetCurrentUser(HttpContext httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        var logger = httpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("UserContextHelper");

        if (httpContext.User?.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? httpContext.User.FindFirst("sub")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                logger?.LogWarning("GetCurrentUser failed: invalid JWT user id claim '{UserIdClaim}' for request {Method} {Path}.", userIdClaim, httpContext.Request.Method, httpContext.Request.Path);
                return null;
            }

            var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
            var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value ?? "kine";
            var fullName = httpContext.User.FindFirst("full_name")?.Value;
            var cabinetIdClaim = httpContext.User.FindFirst("cabinet_id")?.Value;
            int? cabinetId = null;
            if (!string.IsNullOrWhiteSpace(cabinetIdClaim) && int.TryParse(cabinetIdClaim, out var parsedCabinetId))
            {
                cabinetId = parsedCabinetId;
            }

            var needsDbLookup = !cabinetId.HasValue || string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(username);
            if (needsDbLookup)
            {
                using var dbConn = DatabaseConnectionProvider.CreateConnection();
                dbConn.Open();

                using var dbCmd = dbConn.CreateCommand();
                dbCmd.CommandText = @"
                    SELECT username, COALESCE(full_name, ''), COALESCE(role, 'kine'), COALESCE(active, TRUE), cabinet_id
                    FROM users
                    WHERE id = @id
                ";
                dbCmd.Parameters.AddWithValue("@id", userId);

                using var dbReader = dbCmd.ExecuteReader();
                if (dbReader.Read())
                {
                    username = dbReader.IsDBNull(0) ? username : dbReader.GetString(0);
                    fullName = dbReader.IsDBNull(1) ? fullName : dbReader.GetString(1);
                    role = dbReader.IsDBNull(2) ? role : dbReader.GetString(2);
                    var active = dbReader.IsDBNull(3) ? true : dbReader.GetBoolean(3);
                    cabinetId = dbReader.IsDBNull(4) ? cabinetId : dbReader.GetInt32(4);
                }
            }

            return new UserDto
            {
                Id = userId,
                Username = username,
                FullName = fullName,
                Role = role,
                Active = true,
                CabinetId = cabinetId
            };
        }

        if (!httpContext.Request.Headers.TryGetValue(UserIdHeader, out var headerValues))
        {
            logger?.LogWarning("GetCurrentUser failed: missing X-User-Id header for request {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);
            return null;
        }

        var headerValue = headerValues.FirstOrDefault();
        if (!int.TryParse(headerValue, out var userIdFromHeader))
        {
            logger?.LogWarning("GetCurrentUser failed: invalid X-User-Id header value '{HeaderValue}' for request {Method} {Path}.", headerValue, httpContext.Request.Method, httpContext.Request.Path);
            return null;
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, username, COALESCE(full_name, ''), COALESCE(role, 'kine'), COALESCE(active, TRUE), cabinet_id
            FROM users
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@id", userIdFromHeader);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            logger?.LogWarning("GetCurrentUser failed: no user found for X-User-Id={UserId} on request {Method} {Path}.", userIdFromHeader, httpContext.Request.Method, httpContext.Request.Path);
            return null;
        }

        return new UserDto
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            FullName = reader.GetString(2),
            Role = reader.GetString(3),
            Active = reader.GetBoolean(4),
            CabinetId = reader.IsDBNull(5) ? null : reader.GetInt32(5)
        };
    }

    public static bool IsAdmin(HttpContext httpContext)
    {
        var user = GetCurrentUser(httpContext);
        return user?.Role == "admin";
    }

    public static int? GetCurrentCabinetId(HttpContext httpContext)
    {
        var user = GetCurrentUser(httpContext);
        return user?.Role == "admin" ? null : user?.CabinetId;
    }

    public static bool IsPatientAccessible(HttpContext httpContext, int patientId)
    {
        var currentUser = GetCurrentUser(httpContext);
        if (currentUser == null)
        {
            return false;
        }

        if (currentUser.Role == "admin")
        {
            return true;
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT cabinet_id
            FROM patients
            WHERE id = @patientId
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);

        var result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            return false;
        }

        return Convert.ToInt32(result) == currentUser.CabinetId;
    }

    public static bool IsUserCabinetMatch(HttpContext httpContext, int? cabinetId)
    {
        var currentUser = GetCurrentUser(httpContext);
        if (currentUser == null)
        {
            return false;
        }

        return currentUser.Role == "admin" || currentUser.CabinetId == cabinetId;
    }
}
