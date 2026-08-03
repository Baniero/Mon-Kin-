using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using Npgsql;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("kines")]
    public ActionResult<IEnumerable<object>> GetKines()
    {
        var users = new List<object>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, COALESCE(full_name, username, '')
            FROM users
            WHERE role IN ('kine', 'admin') AND COALESCE(active, 1) = 1
            ORDER BY full_name
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new { Id = reader.GetInt32(0), Name = reader.GetString(1) });
        }

        return Ok(users);
    }
}
