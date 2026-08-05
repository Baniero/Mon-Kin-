using System.Linq;
using Microsoft.AspNetCore.Http;
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

        if (!httpContext.Request.Headers.TryGetValue(UserIdHeader, out var headerValues))
        {
            return null;
        }

        if (!int.TryParse(headerValues.FirstOrDefault(), out var userId))
        {
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
        cmd.Parameters.AddWithValue("@id", userId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
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
