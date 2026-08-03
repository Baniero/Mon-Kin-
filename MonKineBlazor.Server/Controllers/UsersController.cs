using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
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
    public ActionResult<UserDto> Create(UserDto user)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (username, full_name, role, active)
            VALUES (@username, @full_name, @role, @active)
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@username", user.Username ?? string.Empty);
        cmd.Parameters.AddWithValue("@full_name", (object?)user.FullName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", user.Role ?? "kine");
        cmd.Parameters.AddWithValue("@active", user.Active);

        user.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, UserDto user)
    {
        if (id != user.Id)
        {
            return BadRequest("L'utilisateur ID ne correspond pas.");
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE users SET
                username = @username,
                full_name = @full_name,
                role = @role,
                active = @active
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@username", user.Username ?? string.Empty);
        cmd.Parameters.AddWithValue("@full_name", (object?)user.FullName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", user.Role ?? "kine");
        cmd.Parameters.AddWithValue("@active", user.Active);
        cmd.Parameters.AddWithValue("@id", id);

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
}
