using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using Npgsql;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult Get()
    {
        try
        {
            using var connection = DatabaseConnectionProvider.CreateConnection();
            connection.Open();
            using var command = new NpgsqlCommand("SELECT 1", connection);
            command.ExecuteScalar();
            return Ok(new { status = "ok", database = "connected" });
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.ToString(), title: "Health check failed", statusCode: 500);
        }
    }
}
