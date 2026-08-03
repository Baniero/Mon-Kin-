using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Shared.Models;
using Npgsql;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<AppointmentDto>> GetAll()
    {
        var appointments = new List<AppointmentDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                a.id,
                a.patient_id,
                COALESCE(p.nom || ' ' || p.prenom, ''),
                a.kine_id,
                COALESCE(u.full_name, u.username, ''),
                a.start_datetime,
                a.end_datetime,
                COALESCE(a.acte, ''),
                COALESCE(a.room, ''),
                COALESCE(a.status, ''),
                COALESCE(a.payment_status, ''),
                COALESCE(a.amount, 0),
                COALESCE(a.paid_amount, 0),
                COALESCE(a.cnam_covered, 0),
                COALESCE(a.notes, '')
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            LEFT JOIN users u ON u.id = a.kine_id
            ORDER BY a.start_datetime DESC
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            appointments.Add(new AppointmentDto
            {
                Id = reader.GetInt32(0),
                PatientId = reader.GetInt32(1),
                PatientName = reader.GetString(2),
                KineId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                KineName = reader.GetString(4),
                Start = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                End = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                Acte = reader.GetString(7),
                Room = reader.GetString(8),
                Status = reader.GetString(9),
                PaymentStatus = reader.GetString(10),
                Amount = reader.GetDecimal(11),
                PaidAmount = reader.GetDecimal(12),
                CnamCovered = reader.GetDecimal(13),
                Notes = reader.GetString(14),
            });
        }

        return Ok(appointments);
    }

    [HttpPost]
    public ActionResult<AppointmentDto> Create(AppointmentDto appointment)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO appointments (
                patient_id, kine_id, start_datetime, end_datetime,
                acte, room, status, payment_status,
                amount, paid_amount, cnam_covered, notes
            ) VALUES (
                @patient_id, @kine_id, @start_datetime, @end_datetime,
                @acte, @room, @status, @payment_status,
                @amount, @paid_amount, @cnam_covered, @notes
            )
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@patient_id", appointment.PatientId);
        cmd.Parameters.AddWithValue("@kine_id", appointment.KineId.HasValue ? (object)appointment.KineId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@start_datetime", appointment.Start ?? DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@end_datetime", appointment.End.HasValue ? (object)appointment.End.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@acte", (object?)appointment.Acte ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@room", (object?)appointment.Room ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)appointment.Status ?? "planifie");
        cmd.Parameters.AddWithValue("@payment_status", (object?)appointment.PaymentStatus ?? "non_paye");
        cmd.Parameters.AddWithValue("@amount", appointment.Amount);
        cmd.Parameters.AddWithValue("@paid_amount", appointment.PaidAmount);
        cmd.Parameters.AddWithValue("@cnam_covered", appointment.CnamCovered);
        cmd.Parameters.AddWithValue("@notes", (object?)appointment.Notes ?? DBNull.Value);

        appointment.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return CreatedAtAction(nameof(GetAll), new { id = appointment.Id }, appointment);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, AppointmentDto appointment)
    {
        if (id != appointment.Id)
        {
            return BadRequest("L'ID du rendez-vous ne correspond pas.");
        }

        if (appointment.KineId.HasValue && appointment.Start.HasValue && appointment.End.HasValue)
        {
            if (HasConflict(appointment.KineId.Value, appointment.Start.Value, appointment.End.Value, id))
            {
                return BadRequest("Conflit de planning pour ce kiné.");
            }
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE appointments SET
                patient_id = @patient_id,
                kine_id = @kine_id,
                start_datetime = @start_datetime,
                end_datetime = @end_datetime,
                acte = @acte,
                room = @room,
                status = @status,
                payment_status = @payment_status,
                amount = @amount,
                paid_amount = @paid_amount,
                cnam_covered = @cnam_covered,
                notes = @notes
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@patient_id", appointment.PatientId);
        cmd.Parameters.AddWithValue("@kine_id", appointment.KineId.HasValue ? (object)appointment.KineId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@start_datetime", appointment.Start ?? DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@end_datetime", appointment.End.HasValue ? (object)appointment.End.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@acte", (object?)appointment.Acte ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@room", (object?)appointment.Room ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)appointment.Status ?? "planifie");
        cmd.Parameters.AddWithValue("@payment_status", (object?)appointment.PaymentStatus ?? "non_paye");
        cmd.Parameters.AddWithValue("@amount", appointment.Amount);
        cmd.Parameters.AddWithValue("@paid_amount", appointment.PaidAmount);
        cmd.Parameters.AddWithValue("@cnam_covered", appointment.CnamCovered);
        cmd.Parameters.AddWithValue("@notes", (object?)appointment.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);

        var rows = cmd.ExecuteNonQuery();
        if (rows == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM appointments WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    private bool HasConflict(int kineId, DateTime start, DateTime end, int? ignoreAppointmentId = null)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        var sql = @"
            SELECT COUNT(*)
            FROM appointments
            WHERE kine_id = @kine_id
              AND @start < COALESCE(end_datetime, start_datetime)
              AND start_datetime < @end
        ";
        if (ignoreAppointmentId.HasValue)
        {
            sql += " AND id <> @ignore_id";
            cmd.Parameters.AddWithValue("@ignore_id", ignoreAppointmentId.Value);
        }

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@kine_id", kineId);
        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end);

        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return count > 0;
    }
}
