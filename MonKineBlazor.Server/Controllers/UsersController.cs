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
    [HttpGet]
    public ActionResult<IEnumerable<UserDto>> GetAll()
    {
        var users = new List<UserDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, username, full_name, COALESCE(role, 'kine'), COALESCE(active, TRUE)
            FROM users
            ORDER BY full_name, username
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new UserDto
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                FullName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Role = reader.GetString(3),
                Active = reader.GetBoolean(4)
            });
        }

        return Ok(users);
    }

    [HttpGet("kines")]
    public ActionResult<IEnumerable<UserDto>> GetKines()
    {
        var users = new List<UserDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, username, full_name, COALESCE(role, 'kine'), COALESCE(active, TRUE)
            FROM users
            WHERE role IN ('kine', 'admin') AND COALESCE(active, TRUE) = TRUE
            ORDER BY full_name, username
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new UserDto
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                FullName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Role = reader.GetString(3),
                Active = reader.GetBoolean(4)
            });
        }

        return Ok(users);
    }

    [HttpGet("{id}")]
    public ActionResult<UserDto> GetById(int id)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, username, full_name, COALESCE(role, 'kine'), COALESCE(active, TRUE)
            FROM users
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return NotFound();
        }

        return Ok(new UserDto
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            FullName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Role = reader.GetString(3),
            Active = reader.GetBoolean(4)
        });
    }

    [HttpPost]
    public ActionResult<UserDto> Create(UserCreateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Le nom d'utilisateur et le mot de passe sont obligatoires.");
        }

        var passwordHash = PasswordHasher.Hash(request.Password);

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (username, full_name, role, active, password_hash)
            VALUES (@username, @full_name, @role, @active, @password_hash)
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@username", request.Username);
        cmd.Parameters.AddWithValue("@full_name", (object?)request.FullName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", request.Role ?? "kine");
        cmd.Parameters.AddWithValue("@active", request.Active);
        cmd.Parameters.AddWithValue("@password_hash", passwordHash);

        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return CreatedAtAction(nameof(GetById), new { id }, new UserDto
        {
            Id = id,
            Username = request.Username,
            FullName = request.FullName,
            Role = request.Role ?? "kine",
            Active = request.Active
        });
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, UserUpdateRequestDto request)
    {
        if (id != request.Id)
        {
            return BadRequest("L'utilisateur ID ne correspond pas.");
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        var sql = @"
            UPDATE users SET
                username = @username,
                full_name = @full_name,
                role = @role,
                active = @active";

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
        cmd.Parameters.AddWithValue("@id", id);

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var newHash = PasswordHasher.Hash(request.Password);
            cmd.Parameters.AddWithValue("@password_hash", newHash);
        }

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

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
            SELECT id, username, full_name, COALESCE(role, 'kine'), COALESCE(active, TRUE), password_hash
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
            Active = active
        });
    }
}
