using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Server.Services;
using MonKineBlazor.Shared.Models;
using Npgsql;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private UserDto? GetCurrentUser() => UserContextHelper.GetCurrentUser(HttpContext);
    private bool IsAdmin() => UserContextHelper.IsAdmin(HttpContext);
    private bool IsPatientAccessible(int patientId) => UserContextHelper.IsPatientAccessible(HttpContext, patientId);

    private bool IsAppointmentAccessible(int appointmentId)
    {
        if (IsAdmin())
        {
            return true;
        }

        var currentUser = GetCurrentUser();
        if (currentUser?.CabinetId == null)
        {
            return false;
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            WHERE a.id = @appointmentId
              AND p.cabinet_id = @cabinet_id
        ";
        cmd.Parameters.AddWithValue("@appointmentId", appointmentId);
        cmd.Parameters.AddWithValue("@cabinet_id", currentUser.CabinetId.Value);

        var result = cmd.ExecuteScalar();
        return result != null;
    }

    [HttpGet]
    public ActionResult<IEnumerable<AppointmentDto>> GetAll()
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var appointments = new List<AppointmentDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (IsAdmin())
        {
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
        }
        else
        {
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
                WHERE p.cabinet_id = @cabinet_id
                ORDER BY a.start_datetime DESC
            ";
            cmd.Parameters.AddWithValue("@cabinet_id", currentUser.CabinetId.HasValue ? (object)currentUser.CabinetId.Value : DBNull.Value);
        }

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

    [HttpGet("patient/{patientId}")]
    public ActionResult<IEnumerable<AppointmentDto>> GetByPatient(int patientId)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        if (!IsAdmin() && !IsPatientAccessible(patientId))
        {
            return Forbid();
        }

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
            WHERE a.patient_id = @patientId
            ORDER BY a.start_datetime DESC
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);

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
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        if (!IsAdmin() && !IsPatientAccessible(appointment.PatientId))
        {
            return Forbid();
        }

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

        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        if (!IsAdmin() && !IsAppointmentAccessible(id))
        {
            return Forbid();
        }

        if (!IsAdmin() && !IsPatientAccessible(appointment.PatientId))
        {
            return Forbid();
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
        ApplyAdvanceAndPaymentForPresentStatus(appointment, conn);

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
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        if (!IsAdmin() && !IsAppointmentAccessible(id))
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM appointments WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    private void ApplyAdvanceAndPaymentForPresentStatus(AppointmentDto appointment, NpgsqlConnection conn)
    {
        if (!string.Equals(appointment.Status, "present", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var remainingAmount = Math.Max(0, appointment.Amount - appointment.PaidAmount);
        var patientAdvance = GetPatientAdvanceBalance(appointment.PatientId, conn);
        var advanceUsed = Math.Min(patientAdvance, remainingAmount);
        if (advanceUsed > 0)
        {
            MarkAdvanceUsage(appointment.Id, appointment.PatientId, advanceUsed, conn);
            UpdatePatientFinanceAdvanceBalance(appointment.PatientId, advanceUsed, conn);
            CreateFinanceLedgerEntry(appointment.PatientId, appointment.Id, "use_advance", -advanceUsed, "avance", "Utilisation d'avance pour séance présente", conn);
            remainingAmount -= advanceUsed;
        }

        // si reste à payer, on considère que le patient encaisse en espèces le jour même
        if (remainingAmount > 0)
        {
            appointment.PaidAmount += remainingAmount;
            appointment.PaymentStatus = ComputePaymentStatus(appointment.Amount, appointment.PaidAmount);
            CreateFinanceLedgerEntry(appointment.PatientId, appointment.Id, "cash_payment", remainingAmount, "espèces", "Paiement espèces renvoyé pour séance présente", conn);
        }
        else
        {
            appointment.PaymentStatus = ComputePaymentStatus(appointment.Amount, appointment.PaidAmount);
        }
    }

    private decimal GetPatientAdvanceBalance(int patientId, NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(advance_balance, 0)
            FROM patient_finance
            WHERE patient_id = @patientId
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);

        var result = cmd.ExecuteScalar();
        if (result != null && result != DBNull.Value)
        {
            return Convert.ToDecimal(result);
        }

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = @"
            SELECT COALESCE(SUM(remaining_amount), 0)
            FROM advance_lots
            WHERE patient_id = @patientId
        ";
        cmd2.Parameters.AddWithValue("@patientId", patientId);

        return Convert.ToDecimal(cmd2.ExecuteScalar());
    }

    private void MarkAdvanceUsage(int appointmentId, int patientId, decimal amountUsed, NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO advance_usage (appointment_id, patient_id, amount_used)
            VALUES (@appointmentId, @patientId, @amountUsed)
            ON CONFLICT (appointment_id) DO UPDATE
            SET amount_used = EXCLUDED.amount_used,
                used_at = CURRENT_TIMESTAMP
        ";
        cmd.Parameters.AddWithValue("@appointmentId", appointmentId);
        cmd.Parameters.AddWithValue("@patientId", patientId);
        cmd.Parameters.AddWithValue("@amountUsed", amountUsed);
        cmd.ExecuteNonQuery();
    }

    private void UpdatePatientFinanceAdvanceBalance(int patientId, decimal amountUsed, NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO patient_finance (patient_id, session_price, patient_share, cnam_share, advance_balance, total_advance_paid)
            VALUES (@patientId, 0, 0, 0, @newBalance, @advancePaid)
            ON CONFLICT (patient_id) DO UPDATE
            SET advance_balance = GREATEST(patient_finance.advance_balance - EXCLUDED.advance_balance, 0),
                total_advance_paid = patient_finance.total_advance_paid + EXCLUDED.total_advance_paid
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);
        cmd.Parameters.AddWithValue("@newBalance", amountUsed);
        cmd.Parameters.AddWithValue("@advancePaid", amountUsed);
        cmd.ExecuteNonQuery();
    }

    private void CreateFinanceLedgerEntry(int patientId, int appointmentId, string entryType, decimal amount, string reference, string note, NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO finance_ledger (patient_id, appointment_id, entry_type, amount, reference, note)
            VALUES (@patientId, @appointmentId, @entryType, @amount, @reference, @note)
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);
        cmd.Parameters.AddWithValue("@appointmentId", appointmentId);
        cmd.Parameters.AddWithValue("@entryType", entryType);
        cmd.Parameters.AddWithValue("@amount", amount);
        cmd.Parameters.AddWithValue("@reference", reference);
        cmd.Parameters.AddWithValue("@note", note);
        cmd.ExecuteNonQuery();
    }

    private static string ComputePaymentStatus(decimal amount, decimal paidAmount)
    {
        if (amount <= 0)
        {
            return "non_paye";
        }

        return paidAmount >= amount ? "paye" : (paidAmount > 0 ? "partiel" : "non_paye");
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
