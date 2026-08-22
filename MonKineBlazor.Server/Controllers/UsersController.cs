using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Server.Services;
using MonKineBlazor.Shared.Models;
using Npgsql;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private bool IsAdmin()
    {
        return UserContextHelper.IsAdmin(HttpContext);
    }

    private bool IsAllowedUser(int id)
    {
        var currentUser = UserContextHelper.GetCurrentUser(HttpContext);
        return currentUser != null && (currentUser.Role == "admin" || currentUser.Id == id);
    }

    private static List<string> ParseAllowedModules(string? modulesJson)
    {
        if (string.IsNullOrWhiteSpace(modulesJson))
        {
            return new List<string>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(modulesJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string SerializeAllowedModules(IEnumerable<string>? modules)
    {
        if (modules == null)
        {
            return "[]";
        }
        return System.Text.Json.JsonSerializer.Serialize(modules.Distinct().Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList());
    }

    [HttpGet]
    public ActionResult<IEnumerable<UserDto>> GetAll()
    {
        var currentUser = UserContextHelper.GetCurrentUser(HttpContext);
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var users = new List<UserDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (currentUser.Role == "admin")
        {
            cmd.CommandText = @"
                SELECT u.id, u.username, u.full_name, COALESCE(u.role, 'kine'), COALESCE(u.active, TRUE), u.cabinet_id, COALESCE(c.nom_cabinet, ''), u.telephone, COALESCE(u.allowed_modules, '[]')
                FROM users u
                LEFT JOIN cabinets c ON c.id = u.cabinet_id
                ORDER BY u.full_name, u.username
            ";
        }
        else if (currentUser.CabinetId.HasValue)
        {
            cmd.CommandText = @"
                SELECT u.id, u.username, u.full_name, COALESCE(u.role, 'kine'), COALESCE(u.active, TRUE), u.cabinet_id, COALESCE(c.nom_cabinet, ''), u.telephone, COALESCE(u.allowed_modules, '[]')
                FROM users u
                LEFT JOIN cabinets c ON c.id = u.cabinet_id
                WHERE u.cabinet_id = @cabinet_id
                ORDER BY u.full_name, u.username
            ";
            cmd.Parameters.AddWithValue("@cabinet_id", currentUser.CabinetId.Value);
        }
        else
        {
            return Forbid();
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new UserDto
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                FullName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Role = reader.GetString(3),
                Active = reader.GetBoolean(4),
                CabinetId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                CabinetName = reader.GetString(6),
                Telephone = reader.IsDBNull(7) ? null : reader.GetString(7),
                Modules = ParseAllowedModules(reader.GetString(8))
            });
        }

        return Ok(users);
    }

    [HttpGet("kines")]
    public ActionResult<IEnumerable<UserDto>> GetKines()
    {
        var currentUser = UserContextHelper.GetCurrentUser(HttpContext);
        var users = new List<UserDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (currentUser?.Role == "admin")
        {
            cmd.CommandText = @"
                SELECT id, username, full_name, COALESCE(role, 'kine'), COALESCE(active, TRUE), cabinet_id, telephone
                FROM users
                WHERE role IN ('kine', 'admin') AND COALESCE(active, TRUE) = TRUE
                ORDER BY full_name, username
            ";
        }
        else
        {
            cmd.CommandText = @"
                SELECT id, username, full_name, COALESCE(role, 'kine'), COALESCE(active, TRUE), cabinet_id, telephone
                FROM users
                WHERE role IN ('kine', 'admin') AND COALESCE(active, TRUE) = TRUE
                  AND cabinet_id = @cabinet_id
                ORDER BY full_name, username
            ";
            cmd.Parameters.AddWithValue("@cabinet_id", currentUser?.CabinetId.HasValue == true ? (object)currentUser.CabinetId.Value : DBNull.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new UserDto
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                FullName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Role = reader.GetString(3),
                Active = reader.GetBoolean(4),
                CabinetId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Telephone = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return Ok(users);
    }

    [HttpGet("{id}")]
    public ActionResult<UserDto> GetById(int id)
    {
        var currentUser = UserContextHelper.GetCurrentUser(HttpContext);
        if (currentUser == null)
        {
            return Unauthorized();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, username, full_name, COALESCE(role, 'kine'), COALESCE(active, TRUE), cabinet_id, telephone, COALESCE(allowed_modules, '[]')
            FROM users
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return NotFound();
        }

        int? targetCabinetId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
        if (currentUser.Role != "admin" && targetCabinetId != currentUser.CabinetId)
        {
            return Forbid();
        }

        return Ok(new UserDto
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            FullName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Role = reader.GetString(3),
            Active = reader.GetBoolean(4),
            CabinetId = targetCabinetId,
            Telephone = reader.IsDBNull(6) ? null : reader.GetString(6),
            Modules = ParseAllowedModules(reader.GetString(7))
        });
    }

    [HttpPost]
    public ActionResult<UserDto> Create(UserCreateRequestDto request)
    {
        var currentUser = UserContextHelper.GetCurrentUser(HttpContext);
        if (!IsAdmin())
        {
            if (currentUser == null)
            {
                request.Role = "kine";
                request.Active = false;
                request.CabinetId = null;
            }
            else if (currentUser.CabinetId == null || request.CabinetId != currentUser.CabinetId)
            {
                return Forbid();
            }
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Le nom d'utilisateur et le mot de passe sont obligatoires.");
        }

        var passwordHash = PasswordHasher.Hash(request.Password);

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (username, full_name, role, active, password_hash, cabinet_id, telephone, allowed_modules)
            VALUES (@username, @full_name, @role, @active, @password_hash, @cabinet_id, @telephone, @allowed_modules)
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@username", request.Username);
        cmd.Parameters.AddWithValue("@full_name", (object?)request.FullName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", request.Role ?? "kine");
        cmd.Parameters.AddWithValue("@active", request.Active);
        cmd.Parameters.AddWithValue("@password_hash", passwordHash);
        cmd.Parameters.AddWithValue("@cabinet_id", (object?)request.CabinetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telephone", (object?)request.Telephone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@allowed_modules", SerializeAllowedModules(request.Modules));

        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return CreatedAtAction(nameof(GetById), new { id }, new UserDto
        {
            Id = id,
            Username = request.Username,
            FullName = request.FullName,
            Role = request.Role ?? "kine",
            Active = request.Active,
            Telephone = request.Telephone,
            Modules = request.Modules
        });
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, UserUpdateRequestDto request)
    {
        if (id != request.Id)
        {
            return BadRequest("L'utilisateur ID ne correspond pas.");
        }

        if (!IsAdmin())
        {
            var currentUser = UserContextHelper.GetCurrentUser(HttpContext);
            if (currentUser == null || currentUser.CabinetId == null || request.CabinetId != currentUser.CabinetId)
            {
                return Forbid();
            }
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        var sql = @"
            UPDATE users SET
                username = @username,
                full_name = @full_name,
                role = @role,
                active = @active,
                cabinet_id = @cabinet_id,
                telephone = @telephone,
                allowed_modules = @allowed_modules";

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            sql += ", password_hash = @password_hash";
        }

        sql += "\r\n            WHERE id = @id\r\n";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@username", request.Username);
        cmd.Parameters.AddWithValue("@full_name", (object?)request.FullName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", request.Role ?? "kine");
        cmd.Parameters.AddWithValue("@active", request.Active);
        cmd.Parameters.AddWithValue("@cabinet_id", (object?)request.CabinetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telephone", (object?)request.Telephone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var newHash = PasswordHasher.Hash(request.Password);
            cmd.Parameters.AddWithValue("@password_hash", newHash);
        }

        cmd.Parameters.AddWithValue("@allowed_modules", SerializeAllowedModules(request.Modules));

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var currentUser = UserContextHelper.GetCurrentUser(HttpContext);
        if (currentUser == null)
        {
            return Unauthorized();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = @"
            SELECT cabinet_id
            FROM users
            WHERE id = @id
        ";
        checkCmd.Parameters.AddWithValue("@id", id);

        var result = checkCmd.ExecuteScalar();
        if (result == null)
        {
            return NotFound();
        }

        int? targetCabinetId = result == DBNull.Value ? null : Convert.ToInt32(result);
        if (currentUser.Role != "admin" && currentUser.CabinetId != targetCabinetId)
        {
            return Forbid();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpPost("login")]
    public ActionResult<UserDto> Login(LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, username, full_name, COALESCE(role, 'kine'), COALESCE(active, TRUE), password_hash, cabinet_id, telephone, COALESCE(allowed_modules, '[]')
            FROM users
            WHERE username = @username
        ";
        cmd.Parameters.AddWithValue("@username", request.Username);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return Unauthorized();
        }

        var id = reader.GetInt32(0);
        var username = reader.GetString(1);
        var fullName = reader.IsDBNull(2) ? null : reader.GetString(2);
        var role = reader.GetString(3);
        var active = reader.GetBoolean(4);
        var passwordHash = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
        int? cabinetId = reader.IsDBNull(6) ? null : reader.GetInt32(6);
        var telephone = reader.IsDBNull(7) ? null : reader.GetString(7);
        var modules = ParseAllowedModules(reader.GetString(8));

        if (!active || string.IsNullOrWhiteSpace(passwordHash) || !PasswordHasher.Verify(passwordHash, request.Password))
        {
            return Unauthorized();
        }

        return Ok(new UserDto
        {
            Id = id,
            Username = username,
            FullName = fullName,
            Role = role,
            Active = active,
            CabinetId = cabinetId,
            Telephone = telephone,
            Modules = modules
        });
    }
}
