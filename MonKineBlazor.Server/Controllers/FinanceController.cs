using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Server.Services;
using MonKineBlazor.Shared.Models;
using Npgsql;
using System.Linq;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FinanceController : ControllerBase
{
    private readonly ILogger<FinanceController> _logger;

    public FinanceController(ILogger<FinanceController> logger)
    {
        _logger = logger;
    }

    private UserDto? GetCurrentUser() => UserContextHelper.GetCurrentUser(HttpContext);
    private bool IsAdmin() => UserContextHelper.IsAdmin(HttpContext);
    private bool IsPatientAccessible(int patientId) => UserContextHelper.IsPatientAccessible(HttpContext, patientId);
    private bool IsAdvanceTransactionAccessible(int transactionId)
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

        var cabinetId = currentUser.CabinetId.Value;

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM advance_transactions t
            JOIN patients p ON p.id = t.patient_id
            WHERE t.id = @transactionId
              AND p.cabinet_id = @cabinet_id
        ";
        cmd.Parameters.AddWithValue("@transactionId", transactionId);
        cmd.Parameters.AddWithValue("@cabinet_id", cabinetId);

        return cmd.ExecuteScalar() != null;
    }

    [HttpGet("cash-closings")]
    public ActionResult<IEnumerable<CashClosingDto>> GetCashClosings(DateTime start, DateTime end)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        int? cabinetId = currentUser.CabinetId;
        if (!IsAdmin() && !cabinetId.HasValue)
        {
            return Forbid();
        }

        var sessionsByDay = new Dictionary<DateTime, decimal>();
        var advancesByDay = new Dictionary<DateTime, decimal>();
        var closingsByDay = new Dictionary<DateTime, CashClosingDto>();

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = IsAdmin() ? @"
                SELECT DATE(a.start_datetime), COALESCE(SUM(a.paid_amount - COALESCE(au.amount_used, 0)), 0)
                FROM appointments a
                LEFT JOIN advance_usage au ON au.appointment_id = a.id
                WHERE DATE(a.start_datetime) BETWEEN @start AND @end
                  AND a.status IN ('present', 'effectue')
                GROUP BY DATE(a.start_datetime)
            " : @"
                SELECT DATE(a.start_datetime), COALESCE(SUM(a.paid_amount - COALESCE(au.amount_used, 0)), 0)
                FROM appointments a
                LEFT JOIN advance_usage au ON au.appointment_id = a.id
                JOIN patients p ON p.id = a.patient_id
                WHERE DATE(a.start_datetime) BETWEEN @start AND @end
                  AND a.status IN ('present', 'effectue')
                  AND p.cabinet_id = @cabinet_id
                GROUP BY DATE(a.start_datetime)
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);
            if (!IsAdmin())
            {
                cmd.Parameters.AddWithValue("@cabinet_id", cabinetId.Value);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sessionsByDay[reader.GetDateTime(0).Date] = reader.GetDecimal(1);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = IsAdmin() ? @"
                SELECT DATE(transaction_date), COALESCE(SUM(amount), 0)
                FROM advance_transactions
                WHERE DATE(transaction_date) BETWEEN @start AND @end
                GROUP BY DATE(transaction_date)
            " : @"
                SELECT DATE(t.transaction_date), COALESCE(SUM(t.amount), 0)
                FROM advance_transactions t
                JOIN patients p ON p.id = t.patient_id
                WHERE DATE(t.transaction_date) BETWEEN @start AND @end
                  AND p.cabinet_id = @cabinet_id
                GROUP BY DATE(t.transaction_date)
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);
            if (!IsAdmin())
            {
                cmd.Parameters.AddWithValue("@cabinet_id", cabinetId.Value);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                advancesByDay[reader.GetDateTime(0).Date] = reader.GetDecimal(1);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT date_jour, COALESCE(expected_amount, 0), COALESCE(actual_amount, 0), COALESCE(validated, FALSE), COALESCE(validated_by, '')
                FROM cash_closings
                WHERE date_jour BETWEEN @start AND @end
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var date = reader.GetDateTime(0).Date;
                closingsByDay[date] = new CashClosingDto
                {
                    DateJour = date,
                    ExpectedAmount = reader.GetDecimal(1),
                    ActualAmount = reader.GetDecimal(2),
                    Diff = reader.GetDecimal(2) - reader.GetDecimal(1),
                    Validated = reader.GetBoolean(3),
                    ValidatedBy = reader.GetString(4)
                };
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT date_jour, COALESCE(expected_amount, 0), COALESCE(actual_amount, 0), COALESCE(validated, FALSE), COALESCE(validated_by, '')
                FROM cash_closings
                WHERE date_jour BETWEEN @start AND @end
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var date = reader.GetDateTime(0).Date;
                closingsByDay[date] = new CashClosingDto
                {
                    DateJour = date,
                    ExpectedAmount = reader.GetDecimal(1),
                    ActualAmount = reader.GetDecimal(2),
                    Diff = reader.GetDecimal(2) - reader.GetDecimal(1),
                    Validated = reader.GetBoolean(3),
                    ValidatedBy = reader.GetString(4)
                };
            }
        }

        var allDays = new SortedSet<DateTime>(sessionsByDay.Keys.Concat(advancesByDay.Keys).Concat(closingsByDay.Keys));
        var rows = new List<CashClosingDto>();
        foreach (var day in allDays)
        {
            var expected = sessionsByDay.GetValueOrDefault(day) + advancesByDay.GetValueOrDefault(day);
            if (!closingsByDay.TryGetValue(day, out var closing))
            {
                closing = new CashClosingDto
                {
                    DateJour = day,
                    ExpectedAmount = expected,
                    ActualAmount = 0,
                    Diff = -expected,
                    Validated = false,
                    ValidatedBy = string.Empty
                };
            }
            else
            {
                closing.ExpectedAmount = expected;
                closing.Diff = closing.ActualAmount - expected;
            }

            rows.Add(closing);
        }

        return Ok(rows.OrderByDescending(r => r.DateJour));
    }

    [HttpPost("cash-closings")]
    public ActionResult<CashClosingDto> ValidateCashClosing(CashClosingRequestDto request)
    {
        var dateJour = request.DateJour.Date;
        var expectedAmount = ComputeExpectedAmount(dateJour, dateJour);
        var actualAmount = request.ActualAmount;
        var validated = Math.Abs(expectedAmount - actualAmount) < 0.01m;
        var validatedBy = string.IsNullOrWhiteSpace(request.ValidatedBy) ? "Web" : request.ValidatedBy;

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cash_closings(date_jour, expected_amount, actual_amount, validated, validated_by)
            VALUES (@dateJour, @expectedAmount, @actualAmount, @validated, @validatedBy)
            ON CONFLICT (date_jour) DO UPDATE SET
                expected_amount = EXCLUDED.expected_amount,
                actual_amount = EXCLUDED.actual_amount,
                validated = EXCLUDED.validated,
                validated_by = EXCLUDED.validated_by
        ";
        cmd.Parameters.AddWithValue("@dateJour", dateJour);
        cmd.Parameters.AddWithValue("@expectedAmount", expectedAmount);
        cmd.Parameters.AddWithValue("@actualAmount", actualAmount);
        cmd.Parameters.AddWithValue("@validated", validated);
        cmd.Parameters.AddWithValue("@validatedBy", validatedBy);
        cmd.ExecuteNonQuery();

        return Ok(new CashClosingDto
        {
            DateJour = dateJour,
            ExpectedAmount = expectedAmount,
            ActualAmount = actualAmount,
            Diff = actualAmount - expectedAmount,
            Validated = validated,
            ValidatedBy = validatedBy
        });
    }

    [HttpDelete("cash-closings/{dateJour:datetime}")]
    public IActionResult DeleteCashClosing(DateTime dateJour)
    {
        if (!IsAdmin())
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM cash_closings
            WHERE date_jour = @dateJour
        ";
        cmd.Parameters.AddWithValue("@dateJour", dateJour.Date);

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpGet("cnam-recovery")]
    public ActionResult<IEnumerable<CnamRecoveryDto>> GetCnamRecovery(DateTime start, DateTime end)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        int? cabinetId = currentUser.CabinetId;
        if (!IsAdmin() && !cabinetId.HasValue)
        {
            return Forbid();
        }

        var rows = new List<CnamRecoveryDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = IsAdmin() ? @"
            SELECT
                COALESCE(p.nom || ' ' || p.prenom, ''),
                COALESCE(p.couverture, ''),
                COUNT(a.id),
                COALESCE(SUM(a.cnam_covered), 0)
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            WHERE DATE(a.start_datetime) BETWEEN @start AND @end
              AND a.status IN ('present', 'effectue')
              AND COALESCE(a.cnam_covered, 0) > 0
            GROUP BY p.id
            ORDER BY COALESCE(SUM(a.cnam_covered), 0) DESC
        " : @"
            SELECT
                COALESCE(p.nom || ' ' || p.prenom, ''),
                COALESCE(p.couverture, ''),
                COUNT(a.id),
                COALESCE(SUM(a.cnam_covered), 0)
            FROM appointments a
            JOIN patients p ON p.id = a.patient_id
            WHERE DATE(a.start_datetime) BETWEEN @start AND @end
              AND a.status IN ('present', 'effectue')
              AND COALESCE(a.cnam_covered, 0) > 0
              AND p.cabinet_id = @cabinet_id
            GROUP BY p.id
            ORDER BY COALESCE(SUM(a.cnam_covered), 0) DESC
        ";
        cmd.Parameters.AddWithValue("@start", start.Date);
        cmd.Parameters.AddWithValue("@end", end.Date);
        if (!IsAdmin())
        {
            cmd.Parameters.AddWithValue("@cabinet_id", cabinetId.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CnamRecoveryDto
            {
                PatientName = reader.GetString(0),
                Couverture = reader.GetString(1),
                NbSeances = reader.GetInt32(2),
                MontantCnam = reader.GetDecimal(3)
            });
        }

        return Ok(rows);
    }

    [HttpGet("cnam-programs")]
    public ActionResult<IEnumerable<CnamProgramInvoiceDto>> GetCnamPrograms(DateTime start, DateTime end)
    {
        try
        {
            var xUserId = Request.Headers["X-User-Id"].FirstOrDefault() ?? "<missing>";
            _logger.LogInformation("GetCnamPrograms called. start={Start}, end={End}, X-User-Id={XUserId}, Path={Path}", start, end, xUserId, Request.Path);

            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                _logger.LogWarning("GetCnamPrograms unauthorized: no current user. X-User-Id={XUserId}", xUserId);
                return Unauthorized();
            }

            int? cabinetId = currentUser.CabinetId;
            if (!IsAdmin() && !cabinetId.HasValue)
            {
                _logger.LogWarning("GetCnamPrograms forbidden: user has no cabinet. X-User-Id={XUserId}", xUserId);
                return Forbid();
            }

            var programs = new List<CnamProgramInvoiceDto>();
            using var conn = DatabaseConnectionProvider.CreateConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = IsAdmin() ? @"
                SELECT
                    pp.id,
                    p.id,
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.couverture, ''),
                    COALESCE(pp.titre, ''),
                    COALESCE(pp.nature_seances, ''),
                    COALESCE(pp.nb_seances, 0),
                    COALESCE(pp.duree_seance_minutes, 0),
                    pp.date_debut,
                    pp.date_fin,
                    COALESCE(pp.prix_unitaire, 0),
                    COALESCE(pp.prix_ttc, 0),
                    COALESCE(pp.code_bureau, ''),
                    COALESCE(pp.annee, ''),
                    COALESCE(pp.numero_decision, ''),
                    COALESCE(pp.numero_ordre, '')
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                WHERE DATE(pp.date_debut) BETWEEN @start AND @end
                  AND COALESCE(p.couverture, '') <> ''
                ORDER BY pp.date_debut DESC
            " : @"
                SELECT
                    pp.id,
                    p.id,
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.couverture, ''),
                    COALESCE(pp.titre, ''),
                    COALESCE(pp.nature_seances, ''),
                    COALESCE(pp.nb_seances, 0),
                    COALESCE(pp.duree_seance_minutes, 0),
                    pp.date_debut,
                    pp.date_fin,
                    COALESCE(pp.prix_unitaire, 0),
                    COALESCE(pp.prix_ttc, 0),
                    COALESCE(pp.code_bureau, ''),
                    COALESCE(pp.annee, ''),
                    COALESCE(pp.numero_decision, ''),
                    COALESCE(pp.numero_ordre, '')
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                WHERE DATE(pp.date_debut) BETWEEN @start AND @end
                  AND COALESCE(p.couverture, '') <> ''
                  AND p.cabinet_id = @cabinet_id
                ORDER BY pp.date_debut DESC
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);
            if (!IsAdmin())
            {
                cmd.Parameters.AddWithValue("@cabinet_id", cabinetId.Value);
            }

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    programs.Add(new CnamProgramInvoiceDto
                    {
                        ProgramId = reader.GetInt32(0),
                        PatientId = reader.GetInt32(1),
                        PatientName = reader.GetString(2),
                        CodePatient = reader.GetString(3),
                        NumeroAssuree = reader.GetString(4),
                        Couverture = reader.GetString(5),
                        NatureSeances = reader.GetString(6),
                        NbSeances = reader.GetInt32(7),
                        DureeSeanceMinutes = reader.GetInt32(8),
                        DateDebut = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        DateFin = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        PrixUnitaire = reader.GetDecimal(11),
                        PrixTTC = reader.GetDecimal(12),
                        CodeBureau = reader.GetString(13),
                        Annee = reader.GetString(14),
                        NumeroDecision = reader.GetString(15),
                        NumeroOrdre = reader.GetString(16),
                        FactureNumber = null
                    });
                }
            }

            var executedNumbers = new Dictionary<(int? CabinetId, int Year), int>();
            using var executedCmd = conn.CreateCommand();
            executedCmd.CommandText = IsAdmin() ? @"
                SELECT p.cabinet_id,
                       EXTRACT(YEAR FROM e.executed_at)::int AS year,
                       COALESCE(MAX((split_part(e.facture_number, '/', 1))::int), 0) AS max_sequence
                FROM cnam_bordereau_executed e
                JOIN patient_programs pp ON pp.id = e.program_id
                JOIN patients p ON p.id = pp.patient_id
                WHERE e.facture_number ~ '^[0-9]+/[0-9]{4}$'
                GROUP BY p.cabinet_id, EXTRACT(YEAR FROM e.executed_at)
            " : @"
                SELECT p.cabinet_id,
                       EXTRACT(YEAR FROM e.executed_at)::int AS year,
                       COALESCE(MAX((split_part(e.facture_number, '/', 1))::int), 0) AS max_sequence
                FROM cnam_bordereau_executed e
                JOIN patient_programs pp ON pp.id = e.program_id
                JOIN patients p ON p.id = pp.patient_id
                WHERE e.facture_number ~ '^[0-9]+/[0-9]{4}$'
                  AND p.cabinet_id = @cabinet_id
                GROUP BY p.cabinet_id, EXTRACT(YEAR FROM e.executed_at)
            ";
            if (!IsAdmin())
            {
                executedCmd.Parameters.AddWithValue("@cabinet_id", cabinetId.Value);
            }

            using var executedReader = executedCmd.ExecuteReader();
            while (executedReader.Read())
            {
                var key = executedReader.IsDBNull(0)
                    ? (int?)null
                    : executedReader.GetInt32(0);
                var year = executedReader.GetInt32(1);
                var maxSequence = executedReader.GetInt32(2);
                executedNumbers[(key, year)] = maxSequence;
            }

            using var detailConn = DatabaseConnectionProvider.CreateConnection();
            detailConn.Open();

            var pendingPrograms = programs
                .OrderBy(p => p.DateDebut ?? DateTime.MaxValue)
                .ToList();

            foreach (var program in pendingPrograms)
            {
                var year = program.DateDebut?.Year ?? DateTime.Today.Year;
                using var cabinetQuery = detailConn.CreateCommand();
                cabinetQuery.CommandText = @"
                    SELECT p.cabinet_id
                    FROM patient_programs pp
                    JOIN patients p ON p.id = pp.patient_id
                    WHERE pp.id = @programId
                ";
                cabinetQuery.Parameters.AddWithValue("@programId", program.ProgramId);
                var cabinetIdObj = cabinetQuery.ExecuteScalar();
                var programCabinetId = cabinetIdObj == DBNull.Value ? (int?)null : Convert.ToInt32(cabinetIdObj);
                var sequenceKey = (programCabinetId, year);
                var currentSequence = executedNumbers.ContainsKey(sequenceKey) ? executedNumbers[sequenceKey] + 1 : 1;
                executedNumbers[sequenceKey] = currentSequence;
                program.FactureNumber = $"{currentSequence:000}/{year}";
            }

            return Ok(programs);
        }
        catch (Exception ex)
        {
            var xUserId = Request.Headers["X-User-Id"].FirstOrDefault() ?? "<missing>";
            _logger.LogError(ex, "GetCnamPrograms failed for X-User-Id={XUserId}, start={Start}, end={End}.", xUserId, start, end);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Erreur interne lors du chargement des programmes CNAM.",
                exception = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    [HttpGet("cnam-bordereau")]
    public ActionResult<IEnumerable<CnamBordereauEntryDto>> GetCnamBordereau(DateTime start, DateTime end)
    {
        var xUserId = Request.Headers["X-User-Id"].FirstOrDefault() ?? "<missing>";
        _logger.LogInformation("GetCnamBordereau called. start={Start}, end={End}, X-User-Id={XUserId}, Path={Path}", start, end, xUserId, Request.Path);

        try
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                _logger.LogWarning("GetCnamBordereau unauthorized. Missing current user. X-User-Id={XUserId}", xUserId);
                return Unauthorized();
            }

            int? currentCabinetId = currentUser.CabinetId;
            if (!IsAdmin() && !currentCabinetId.HasValue)
            {
                _logger.LogWarning("GetCnamBordereau forbidden. User has no cabinet and is not admin. X-User-Id={XUserId}", xUserId);
                return Forbid();
            }

            var pendingRows = new List<(int ProgramId, DateTime? DateDebut, string CodePatient, string NumeroAssuree, string PatientName, decimal TotalTTC, int? CabinetId)>();
            using var conn = DatabaseConnectionProvider.CreateConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = IsAdmin() ? @"
                SELECT
                    pp.id,
                    pp.date_debut,
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(pp.prix_ttc, 0),
                    p.cabinet_id
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                WHERE DATE(pp.date_debut) BETWEEN @start AND @end
                  AND COALESCE(p.couverture, '') <> ''
                  AND COALESCE(p.n_assuree, '') <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM cnam_bordereau_executed e WHERE e.program_id = pp.id
                  )
                ORDER BY pp.date_debut
            " : @"
                SELECT
                    pp.id,
                    pp.date_debut,
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(pp.prix_ttc, 0),
                    p.cabinet_id
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                WHERE DATE(pp.date_debut) BETWEEN @start AND @end
                  AND COALESCE(p.couverture, '') <> ''
                  AND COALESCE(p.n_assuree, '') <> ''
                  AND p.cabinet_id = @cabinet_id
                  AND NOT EXISTS (
                      SELECT 1 FROM cnam_bordereau_executed e WHERE e.program_id = pp.id
                  )
                ORDER BY pp.date_debut
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);
            if (!IsAdmin())
            {
                cmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
            }

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    pendingRows.Add((
                        ProgramId: reader.GetInt32(0),
                        DateDebut: reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1),
                        CodePatient: reader.GetString(2),
                        NumeroAssuree: reader.GetString(3),
                        PatientName: reader.GetString(4),
                        TotalTTC: reader.GetDecimal(5),
                        CabinetId: reader.IsDBNull(6) ? null : reader.GetInt32(6)
                    ));
                }
            }

            var sequences = new Dictionary<(int? CabinetId, int Year), int>();
            using var seqCmd = conn.CreateCommand();
            seqCmd.CommandText = IsAdmin() ? @"
                SELECT
                    p.cabinet_id,
                    EXTRACT(YEAR FROM e.executed_at)::int AS year,
                    COALESCE(MAX((split_part(e.facture_number, '/', 1))::int), 0) AS max_sequence
                FROM cnam_bordereau_executed e
                JOIN patient_programs pp ON pp.id = e.program_id
                JOIN patients p ON p.id = pp.patient_id
                WHERE e.facture_number ~ '^[0-9]+/[0-9]{4}$'
                GROUP BY p.cabinet_id, EXTRACT(YEAR FROM e.executed_at)
            " : @"
                SELECT
                    p.cabinet_id,
                    EXTRACT(YEAR FROM e.executed_at)::int AS year,
                    COALESCE(MAX((split_part(e.facture_number, '/', 1))::int), 0) AS max_sequence
                FROM cnam_bordereau_executed e
                JOIN patient_programs pp ON pp.id = e.program_id
                JOIN patients p ON p.id = pp.patient_id
                WHERE e.facture_number ~ '^[0-9]+/[0-9]{4}$'
                  AND p.cabinet_id = @cabinet_id
                GROUP BY p.cabinet_id, EXTRACT(YEAR FROM e.executed_at)
            ";
            if (!IsAdmin())
            {
                seqCmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
            }

            using var seqReader = seqCmd.ExecuteReader();
            while (seqReader.Read())
            {
                var cabinetId = seqReader.IsDBNull(0) ? null : (int?)seqReader.GetInt32(0);
                var year = seqReader.GetInt32(1);
                var maxSequence = seqReader.GetInt32(2);
                sequences[(cabinetId, year)] = maxSequence;
            }

            var rows = new List<CnamBordereauEntryDto>();
            foreach (var item in pendingRows.OrderBy(x => x.DateDebut ?? DateTime.MaxValue))
            {
                var year = item.DateDebut?.Year ?? DateTime.Today.Year;
                var key = (item.CabinetId, year);
                var currentSequence = sequences.ContainsKey(key) ? sequences[key] + 1 : 1;
                sequences[key] = currentSequence;

                rows.Add(new CnamBordereauEntryDto
                {
                    ProgramId = item.ProgramId,
                    FactureNumber = $"{currentSequence:000}/{year}",
                    DateFacture = item.DateDebut,
                    CodePatient = item.CodePatient,
                    NumeroAssuree = item.NumeroAssuree,
                    PatientName = item.PatientName,
                    TotalTTC = item.TotalTTC
                });
            }

            _logger.LogInformation("GetCnamBordereau returning {Count} entries for user {UserId}.", rows.Count, xUserId);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCnamBordereau failed for X-User-Id={XUserId}, start={Start}, end={End}.", xUserId, start, end);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Erreur interne lors du chargement des factures CNAM.",
                exception = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    [HttpGet("cnam-bordereau-executed")]
    public ActionResult<IEnumerable<CnamBordereauEntryDto>> GetCnamBordereauExecuted(DateTime start, DateTime end)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        int? currentCabinetId = currentUser.CabinetId;
        if (!IsAdmin() && !currentCabinetId.HasValue)
        {
            return Forbid();
        }

        var rows = new List<CnamBordereauEntryDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = IsAdmin() ? @"
            SELECT
                pp.id,
                COALESCE(e.bordereau_number, 0),
                COALESCE(pp.code_bureau, ''),
                COALESCE(p.n_assuree, ''),
                COALESCE(p.nom || ' ' || p.prenom, ''),
                COALESCE(pp.prix_ttc, 0),
                e.executed_at,
                COALESCE(e.executed_by, ''),
                COALESCE(e.facture_number, ''),
                pp.date_debut
            FROM cnam_bordereau_executed e
            JOIN patient_programs pp ON pp.id = e.program_id
            JOIN patients p ON p.id = pp.patient_id
            WHERE DATE(e.executed_at) BETWEEN @start AND @end
            ORDER BY e.executed_at DESC
        " : @"
            SELECT
                pp.id,
                COALESCE(e.bordereau_number, 0),
                COALESCE(pp.code_bureau, ''),
                COALESCE(p.n_assuree, ''),
                COALESCE(p.nom || ' ' || p.prenom, ''),
                COALESCE(pp.prix_ttc, 0),
                e.executed_at,
                COALESCE(e.executed_by, ''),
                COALESCE(e.facture_number, ''),
                pp.date_debut
            FROM cnam_bordereau_executed e
            JOIN patient_programs pp ON pp.id = e.program_id
            JOIN patients p ON p.id = pp.patient_id
            WHERE DATE(e.executed_at) BETWEEN @start AND @end
              AND p.cabinet_id = @cabinet_id
            ORDER BY e.executed_at DESC
        ";
        cmd.Parameters.AddWithValue("@start", start.Date);
        cmd.Parameters.AddWithValue("@end", end.Date);
        if (!IsAdmin())
        {
            cmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CnamBordereauEntryDto
            {
                ProgramId = reader.GetInt32(0),
                BordereauNumber = reader.GetInt32(1),
                CodePatient = reader.GetString(2),
                NumeroAssuree = reader.GetString(3),
                PatientName = reader.GetString(4),
                TotalTTC = reader.GetDecimal(5),
                ExecutedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                ExecutedBy = reader.GetString(7),
                FactureNumber = reader.GetString(8),
                DateFacture = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9)
            });
        }

        return Ok(rows);
    }

    [HttpDelete("cnam-bordereau-executed/bordereau/{bordereauNumber}")]
    public IActionResult DeleteExecutedCnamBordereauByBordereauNumber(int bordereauNumber, string? mode = null)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        int? currentCabinetId = currentUser.CabinetId;
        if (!IsAdmin() && !currentCabinetId.HasValue)
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction();

        using var checkCmd = conn.CreateCommand();
        checkCmd.Transaction = transaction;
        checkCmd.CommandText = IsAdmin() ? @"
            SELECT 1
            FROM cnam_bordereau_executed e
            JOIN patient_programs pp ON pp.id = e.program_id
            JOIN patients p ON p.id = pp.patient_id
            WHERE e.bordereau_number = @bordereauNumber
        " : @"
            SELECT 1
            FROM cnam_bordereau_executed e
            JOIN patient_programs pp ON pp.id = e.program_id
            JOIN patients p ON p.id = pp.patient_id
            WHERE e.bordereau_number = @bordereauNumber
              AND p.cabinet_id = @cabinet_id
        ";
        checkCmd.Parameters.AddWithValue("@bordereauNumber", bordereauNumber);
        if (!IsAdmin())
        {
            checkCmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
        }

        var exists = checkCmd.ExecuteScalar();
        if (exists == null || exists == DBNull.Value)
        {
            return NotFound("Aucun bordereau CNAM trouvé pour ce numéro.");
        }

        using var deleteCmd = conn.CreateCommand();
        deleteCmd.Transaction = transaction;
        deleteCmd.CommandText = @"
            DELETE FROM cnam_bordereau_executed
            WHERE bordereau_number = @bordereauNumber
        ";
        deleteCmd.Parameters.AddWithValue("@bordereauNumber", bordereauNumber);
        var rowsAffected = deleteCmd.ExecuteNonQuery();

        transaction.Commit();

        if (rowsAffected == 0)
        {
            return NotFound("Aucun bordereau CNAM trouvé à supprimer.");
        }

        var action = string.Equals(mode, "hard", StringComparison.OrdinalIgnoreCase)
            ? "supprimé définitivement"
            : "annulé";
        return Ok(new { message = $"Bordereau CNAM {action}." });
    }

    [HttpPost("cnam-bordereau/execute")]
    [HttpPost("cnam-bordereau-execute")]
    [Consumes("application/json")]
    public ActionResult<CnamBordereauEntryDto> ExecuteCnamBordereau([FromBody] CnamBordereauExecuteRequestDto request)
        {
            if (request == null)
            {
                _logger.LogWarning("ExecuteCnamBordereau called with null request.");
                return BadRequest("Le corps de la requête est requis.");
            }

            var currentUser = GetCurrentUser();
            _logger.LogInformation("ExecuteCnamBordereau called. ProgramId={ProgramId}, ExecutedBy={ExecutedBy}, UserId={UserId}, CabinetId={CabinetId}",
                request.ProgramId,
                request.ExecutedBy,
                currentUser?.Id ?? -1,
                currentUser?.CabinetId);

            if (currentUser == null)
            {
                var hasHeader = Request.Headers.ContainsKey("X-User-Id");
                var headerValue = hasHeader ? Request.Headers["X-User-Id"].FirstOrDefault() : string.Empty;
                _logger.LogWarning("ExecuteCnamBordereau unauthorized: current user is null. X-User-Id present={HasHeader}, value={HeaderValue}", hasHeader, headerValue);
                return Unauthorized();
            }

            using var conn = DatabaseConnectionProvider.CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            int? currentCabinetId = currentUser.CabinetId;
            if (!IsAdmin() && !currentCabinetId.HasValue)
            {
                return Forbid();
            }

            using var checkCmd = conn.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = IsAdmin() ? @"
                SELECT p.cabinet_id
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                WHERE pp.id = @programId
                  AND COALESCE(p.couverture, '') <> ''
                  AND COALESCE(p.n_assuree, '') <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM cnam_bordereau_executed e WHERE e.program_id = pp.id
                  )
            " : @"
                SELECT p.cabinet_id
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                WHERE pp.id = @programId
                  AND COALESCE(p.couverture, '') <> ''
                  AND COALESCE(p.n_assuree, '') <> ''
                  AND p.cabinet_id = @cabinet_id
                  AND NOT EXISTS (
                      SELECT 1 FROM cnam_bordereau_executed e WHERE e.program_id = pp.id
                  )
            ";
            checkCmd.Parameters.AddWithValue("@programId", request.ProgramId);
            if (!IsAdmin())
            {
                if (currentUser.CabinetId == null)
                {
                    return Forbid();
                }
                checkCmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
            }
            var cabinetIdObj = checkCmd.ExecuteScalar();
            if (cabinetIdObj == null || cabinetIdObj == DBNull.Value)
            {
                _logger.LogWarning("ExecuteCnamBordereau failed: programme introuvable ou déjà exécuté. ProgramId={ProgramId}, UserId={UserId}, CabinetId={CabinetId}",
                    request.ProgramId,
                    currentUser.Id,
                    currentUser.CabinetId);
                return BadRequest("Programme introuvable ou déjà exécuté.");
            }

            var programCabinetId = Convert.ToInt32(cabinetIdObj);
            var invoiceYear = DateTime.UtcNow.Year;
            using var sequenceCmd = conn.CreateCommand();
            sequenceCmd.Transaction = transaction;
            sequenceCmd.CommandText = @"
                SELECT COALESCE(MAX((split_part(facture_number, '/', 1))::int), 0)
                FROM cnam_bordereau_executed e
                JOIN patient_programs pp ON pp.id = e.program_id
                JOIN patients p ON p.id = pp.patient_id
                WHERE p.cabinet_id = @cabinet_id
                  AND EXTRACT(YEAR FROM e.executed_at) = @year
                  AND e.facture_number ~ '^[0-9]+/[0-9]{4}$'
            ";
            sequenceCmd.Parameters.AddWithValue("@cabinet_id", programCabinetId);
            sequenceCmd.Parameters.AddWithValue("@year", invoiceYear);
            var currentInvoiceNumberObj = sequenceCmd.ExecuteScalar();
            var nextInvoiceSequence = Convert.ToInt32(currentInvoiceNumberObj ?? 0) + 1;

            using var bordereauSequenceCmd = conn.CreateCommand();
            bordereauSequenceCmd.Transaction = transaction;
            bordereauSequenceCmd.CommandText = @"
                SELECT COALESCE(MAX(e.bordereau_number), 0)
                FROM cnam_bordereau_executed e
                JOIN patient_programs pp ON pp.id = e.program_id
                JOIN patients p ON p.id = pp.patient_id
                WHERE p.cabinet_id = @cabinet_id
                  AND EXTRACT(YEAR FROM e.executed_at) = @year
            ";
            bordereauSequenceCmd.Parameters.AddWithValue("@cabinet_id", programCabinetId);
            bordereauSequenceCmd.Parameters.AddWithValue("@year", invoiceYear);
            var currentBordereauNumberObj = bordereauSequenceCmd.ExecuteScalar();
            var nextBordereauNumber = Convert.ToInt32(currentBordereauNumberObj ?? 0) + 1;
            var factureNumber = $"{nextInvoiceSequence:000}/{invoiceYear}";

            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = @"
                INSERT INTO cnam_bordereau_executed(program_id, executed_at, executed_by, bordereau_number, facture_number)
                VALUES (@programId, NOW(), @executedBy, @bordereauNumber, @factureNumber)
                ON CONFLICT (program_id) DO NOTHING
            ";
            insertCmd.Parameters.AddWithValue("@programId", request.ProgramId);
            insertCmd.Parameters.AddWithValue("@executedBy", request.ExecutedBy ?? "Web");
            insertCmd.Parameters.AddWithValue("@bordereauNumber", nextBordereauNumber);
            insertCmd.Parameters.AddWithValue("@factureNumber", factureNumber);
            var rowsAffected = insertCmd.ExecuteNonQuery();
            _logger.LogInformation("ExecuteCnamBordereau insert executed. ProgramId={ProgramId}, FactureNumber={FactureNumber}, RowsAffected={RowsAffected}",
                request.ProgramId,
                factureNumber,
                rowsAffected);

            using var fetchCmd = conn.CreateCommand();
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = IsAdmin() ? @"
                SELECT
                    pp.id,
                    COALESCE(e.bordereau_number, 0),
                    pp.date_debut,
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(pp.prix_ttc, 0),
                    e.executed_at,
                    COALESCE(e.executed_by, ''),
                    COALESCE(e.facture_number, '')
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                JOIN cnam_bordereau_executed e ON e.program_id = pp.id
                WHERE pp.id = @programId
            " : @"
                SELECT
                    pp.id,
                    COALESCE(e.bordereau_number, 0),
                    pp.date_debut,
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(pp.prix_ttc, 0),
                    e.executed_at,
                    COALESCE(e.executed_by, ''),
                    COALESCE(e.facture_number, '')
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                JOIN cnam_bordereau_executed e ON e.program_id = pp.id
                WHERE pp.id = @programId
                  AND p.cabinet_id = @cabinet_id
            ";
            fetchCmd.Parameters.AddWithValue("@programId", request.ProgramId);
            if (!IsAdmin())
            {
                if (currentUser.CabinetId == null)
                {
                    return Forbid();
                }
                fetchCmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
            }

            CnamBordereauEntryDto executedEntry;
            using (var reader = fetchCmd.ExecuteReader())
            {
                if (!reader.Read())
                {
                    _logger.LogWarning("ExecuteCnamBordereau fetch returned no row. ProgramId={ProgramId}, UserId={UserId}, CabinetId={CabinetId}",
                        request.ProgramId,
                        currentUser.Id,
                        currentUser.CabinetId);
                    return NotFound();
                }

                var dateDebut = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);
                executedEntry = new CnamBordereauEntryDto
                {
                    ProgramId = reader.GetInt32(0),
                    BordereauNumber = reader.GetInt32(1),
                    FactureNumber = reader.GetString(9),
                    DateFacture = dateDebut,
                    CodePatient = reader.GetString(3),
                    NumeroAssuree = reader.GetString(4),
                    PatientName = reader.GetString(5),
                    TotalTTC = reader.GetDecimal(6),
                    ExecutedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    ExecutedBy = reader.GetString(8)
                };
            }

            transaction.Commit();
            return Ok(executedEntry);
        }

    [HttpPost("cnam-bordereau-execute-bulk")]
    [Consumes("application/json")]
    public ActionResult<IEnumerable<CnamBordereauEntryDto>> ExecuteCnamBordereauBulk([FromBody] CnamBordereauExecuteBulkRequestDto request)
    {
        if (request == null || request.ProgramIds == null || !request.ProgramIds.Any())
        {
            return BadRequest("La liste des programmes à exécuter est requise.");
        }

        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction();

        int? currentCabinetId = currentUser.CabinetId;
        if (!IsAdmin() && !currentCabinetId.HasValue)
        {
            return Forbid();
        }

        using var checkCmd = conn.CreateCommand();
        checkCmd.Transaction = transaction;
        checkCmd.CommandText = @"
            SELECT pp.id
            FROM patient_programs pp
            JOIN patients p ON p.id = pp.patient_id
            LEFT JOIN cnam_bordereau_executed e ON e.program_id = pp.id
            WHERE pp.id = ANY(@programIds)
              AND COALESCE(p.couverture, '') <> ''
              AND COALESCE(p.n_assuree, '') <> ''
              AND e.program_id IS NULL
        ";
        if (!IsAdmin())
        {
            checkCmd.CommandText += " AND p.cabinet_id = @cabinet_id";
            checkCmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
        }
        checkCmd.Parameters.AddWithValue("@programIds", request.ProgramIds.ToArray());

        var validPrograms = new HashSet<int>();
        using (var reader = checkCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                validPrograms.Add(reader.GetInt32(0));
            }
        }

        if (!validPrograms.SetEquals(request.ProgramIds))
        {
            return BadRequest("Certains programmes sont invalides ou déjà exécutés.");
        }

        var invoiceYear = DateTime.UtcNow.Year;
        using var invoiceSequenceCmd = conn.CreateCommand();
        invoiceSequenceCmd.Transaction = transaction;
        invoiceSequenceCmd.CommandText = @"
            SELECT COALESCE(MAX((split_part(facture_number, '/', 1))::int), 0)
            FROM cnam_bordereau_executed e
            JOIN patient_programs pp ON pp.id = e.program_id
            JOIN patients p ON p.id = pp.patient_id
            WHERE p.cabinet_id = @cabinet_id
              AND EXTRACT(YEAR FROM e.executed_at) = @year
              AND e.facture_number ~ '^[0-9]+/[0-9]{4}$'
        ";
        invoiceSequenceCmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
        invoiceSequenceCmd.Parameters.AddWithValue("@year", invoiceYear);
        var currentInvoiceNumberObj = invoiceSequenceCmd.ExecuteScalar();
        var nextInvoiceSequence = Convert.ToInt32(currentInvoiceNumberObj ?? 0) + 1;

        using var bordereauSequenceCmd = conn.CreateCommand();
        bordereauSequenceCmd.Transaction = transaction;
        bordereauSequenceCmd.CommandText = @"
            SELECT COALESCE(MAX(e.bordereau_number), 0)
            FROM cnam_bordereau_executed e
            JOIN patient_programs pp ON pp.id = e.program_id
            JOIN patients p ON p.id = pp.patient_id
            WHERE p.cabinet_id = @cabinet_id
              AND EXTRACT(YEAR FROM e.executed_at) = @year
        ";
        bordereauSequenceCmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
        bordereauSequenceCmd.Parameters.AddWithValue("@year", invoiceYear);
        var currentBordereauNumberObj = bordereauSequenceCmd.ExecuteScalar();
        var nextBordereauNumber = Convert.ToInt32(currentBordereauNumberObj ?? 0) + 1;

        using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = @"
            INSERT INTO cnam_bordereau_executed(program_id, executed_at, executed_by, bordereau_number, facture_number)
            VALUES (@programId, NOW(), @executedBy, @bordereauNumber, @factureNumber)
        ";
        insertCmd.Parameters.Add(new NpgsqlParameter("@programId", NpgsqlTypes.NpgsqlDbType.Integer));
        insertCmd.Parameters.AddWithValue("@executedBy", request.ExecutedBy ?? "Web");
        insertCmd.Parameters.AddWithValue("@bordereauNumber", nextBordereauNumber);
        insertCmd.Parameters.AddWithValue("@factureNumber", string.Empty);

        var invoiceSequence = nextInvoiceSequence;
        foreach (var programId in request.ProgramIds)
        {
            invoiceSequence++;
            var factureNumber = $"{invoiceSequence:000}/{invoiceYear}";
            insertCmd.Parameters["@programId"].Value = programId;
            insertCmd.Parameters["@factureNumber"].Value = factureNumber;
            insertCmd.ExecuteNonQuery();
        }

        var results = new List<CnamBordereauEntryDto>();
        using var fetchCmd = conn.CreateCommand();
        fetchCmd.Transaction = transaction;
        fetchCmd.CommandText = @"
            SELECT
                pp.id,
                COALESCE(e.bordereau_number, 0),
                pp.date_debut,
                COALESCE(p.code_patient, ''),
                COALESCE(p.n_assuree, ''),
                COALESCE(p.nom || ' ' || p.prenom, ''),
                COALESCE(pp.prix_ttc, 0),
                e.executed_at,
                COALESCE(e.executed_by, ''),
                COALESCE(e.facture_number, '')
            FROM patient_programs pp
            JOIN patients p ON p.id = pp.patient_id
            JOIN cnam_bordereau_executed e ON e.program_id = pp.id
            WHERE e.bordereau_number = @bordereauNumber
              AND EXTRACT(YEAR FROM e.executed_at) = @year
        ";
        fetchCmd.Parameters.AddWithValue("@bordereauNumber", nextBordereauNumber);
        fetchCmd.Parameters.AddWithValue("@year", invoiceYear);

        using (var reader = fetchCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                results.Add(new CnamBordereauEntryDto
                {
                    ProgramId = reader.GetInt32(0),
                    BordereauNumber = reader.GetInt32(1),
                    DateFacture = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2),
                    CodePatient = reader.GetString(3),
                    NumeroAssuree = reader.GetString(4),
                    PatientName = reader.GetString(5),
                    TotalTTC = reader.GetDecimal(6),
                    ExecutedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    ExecutedBy = reader.GetString(8),
                    FactureNumber = reader.GetString(9)
                });
            }
        }

        transaction.Commit();
        return Ok(results);
    }

    [HttpOptions("cnam-bordereau/execute")]
    [HttpOptions("cnam-bordereau-execute")]
    public IActionResult OptionsCnamBordereauExecute()
    {
        return Ok();
    }

    [HttpGet("cnam-bordereau-text")]
    public ActionResult GetCnamBordereauText(DateTime start, DateTime end, int? bordereauNumber = null)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        int? currentCabinetId = currentUser.CabinetId;
        if (!IsAdmin() && !currentCabinetId.HasValue)
        {
            return Forbid();
        }

        var cabinet = new CabinetInfoDto();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cabinetCmd = conn.CreateCommand();
        cabinetCmd.CommandText = @"
            SELECT racine, cle
            FROM cabinets
            WHERE id = @cabinetId
            LIMIT 1
        ";
        cabinetCmd.Parameters.AddWithValue("@cabinetId", currentCabinetId ?? 0);

        using (var cabinetReader = cabinetCmd.ExecuteReader())
        {
            if (cabinetReader.Read())
            {
                cabinet.NumeroEmployeur = cabinetReader.IsDBNull(0) ? string.Empty : cabinetReader.GetString(0);
                cabinet.CodeCnam = cabinetReader.IsDBNull(1) ? string.Empty : cabinetReader.GetString(1);
            }
            else
            {
                cabinet.CodeCnam = string.Empty;
                cabinet.NumeroEmployeur = string.Empty;
            }
        }

        var rows = new List<(string FactureNumber, string CodeBureau, string Annee, string NumeroDecision, string NumeroAssuree, int NbSeances, DateTime? DateDebut, DateTime? DateFin, DateTime? DateFacture, decimal TotalTTC, int BordereauNumber)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                COALESCE(e.facture_number, ''),
                COALESCE(pp.code_bureau, ''),
                COALESCE(pp.annee, ''),
                COALESCE(pp.numero_decision, ''),
                COALESCE(p.n_assuree, ''),
                COALESCE(pp.nb_seances, 0),
                pp.date_debut,
                pp.date_fin,
                e.executed_at,
                COALESCE(pp.prix_ttc, 0),
                COALESCE(e.bordereau_number, 0)
            FROM cnam_bordereau_executed e
            JOIN patient_programs pp ON pp.id = e.program_id
            JOIN patients p ON p.id = pp.patient_id
            WHERE DATE(e.executed_at) BETWEEN @start AND @end
        ";
        if (!IsAdmin())
        {
            cmd.CommandText += " AND p.cabinet_id = @cabinet_id";
            cmd.Parameters.AddWithValue("@cabinet_id", currentCabinetId.Value);
        }
        if (bordereauNumber.HasValue)
        {
            cmd.CommandText += " AND e.bordereau_number = @bordereauNumber";
            cmd.Parameters.AddWithValue("@bordereauNumber", bordereauNumber.Value);
        }
        cmd.CommandText += " ORDER BY e.executed_at, e.facture_number";
        cmd.Parameters.AddWithValue("@start", start.Date);
        cmd.Parameters.AddWithValue("@end", end.Date);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(
                (
                    FactureNumber: reader.GetString(0),
                    CodeBureau: reader.GetString(1),
                    Annee: reader.GetString(2),
                    NumeroDecision: reader.GetString(3),
                    NumeroAssuree: reader.GetString(4),
                    NbSeances: reader.GetInt32(5),
                    DateDebut: reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    DateFin: reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    DateFacture: reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    TotalTTC: reader.GetDecimal(9),
                    BordereauNumber: reader.GetInt32(10)
                )
            );
        }

        var textBuilder = new System.Text.StringBuilder();
        var bordereauYear = start.Year;
        var (codeCnam1, codeCnam2, codeCnam3) = SplitCabinetCode(cabinet.CodeCnam);
        var (employerNumber1, employerNumber2) = SplitEmployerNumber(cabinet.NumeroEmployeur);
        var selectedBordereauNumberText = rows.Any() ? rows.First().BordereauNumber.ToString("000") : "000";
        var totalFactures = rows.Count.ToString("000000");
        var totalTtcMillimes = (long)Math.Round(rows.Sum(r => r.TotalTTC) * 1000m, MidpointRounding.AwayFromZero);
        var totalTtcText = totalTtcMillimes.ToString().PadLeft(12, '0');

        var header = new char[139];
        for (int i = 0; i < header.Length; i++) header[i] = '0';
        header[0] = '1';

        var yearText = bordereauYear.ToString("0000");
        for (int i = 0; i < 4; i++) header[1 + i] = yearText[i];

        for (int i = 0; i < 3; i++) header[5 + i] = selectedBordereauNumberText[i];

        for (int i = 0; i < 2; i++) header[8 + i] = codeCnam1[i];
        for (int i = 0; i < 8; i++) header[10 + i] = codeCnam2[i];
        for (int i = 0; i < 2; i++) header[18 + i] = codeCnam3[i];

        for (int i = 0; i < 32; i++) header[20 + i] = '0';

        for (int i = 0; i < 11; i++) header[52 + i] = employerNumber1[i];
        for (int i = 0; i < 2; i++) header[63 + i] = employerNumber2[i];

        for (int i = 0; i < 6; i++) header[65 + i] = totalFactures[i];

        for (int i = 0; i < 14; i++) header[71 + i] = '0';

        for (int i = 0; i < 12; i++) header[85 + i] = totalTtcText[i];

        for (int i = 97; i < 135; i++) header[i] = '0';

        textBuilder.AppendLine(new string(header));

        foreach (var row in rows)
        {
            var line = new char[139];
            for (int i = 0; i < line.Length; i++) line[i] = '0';
            line[0] = '2';

            for (int i = 0; i < 4; i++) line[1 + i] = yearText[i];
            for (int i = 0; i < 3; i++) line[5 + i] = selectedBordereauNumberText[i];
            for (int i = 0; i < 2; i++) line[8 + i] = codeCnam1[i];
            for (int i = 0; i < 8; i++) line[10 + i] = codeCnam2[i];
            for (int i = 0; i < 2; i++) line[18 + i] = codeCnam3[i];

            var exerciceYear = !string.IsNullOrWhiteSpace(row.Annee) && row.Annee.Length == 4 ? row.Annee : yearText;
            for (int i = 0; i < 4; i++) line[20 + i] = exerciceYear[i];

            for (int i = 24; i <= 31; i++) line[i] = ' ';

            var factureNumber = FormatFactureNumber(row.FactureNumber);
            for (int i = 0; i < factureNumber.Length; i++) line[32 + i] = factureNumber[i];

            var bordereauText = row.BordereauNumber.ToString("000");
            for (int i = 0; i < bordereauText.Length; i++) line[40 + i] = bordereauText[i];

            var codeAct = "4375";
            for (int i = 0; i < codeAct.Length; i++) line[43 + i] = codeAct[i];

            var (decisionYear, decisionKey) = SplitDecisionParts(row.NumeroDecision, bordereauYear);
            for (int i = 0; i < 4; i++) line[47 + i] = decisionYear[i];
            for (int i = 0; i < 6; i++) line[51 + i] = decisionKey[i];

            var numeroAssuree = NormalizeDigits(row.NumeroAssuree, 12);
            for (int i = 0; i < 12; i++) line[57 + i] = numeroAssuree[i];

            var codeQualite = "003";
            for (int i = 0; i < 3; i++) line[69 + i] = codeQualite[i];

            var nbSeances = row.NbSeances.ToString("000");
            for (int i = 0; i < 3; i++) line[72 + i] = nbSeances[i];

            var debutText = row.DateDebut.HasValue ? row.DateDebut.Value.ToString("yyyyMMdd") : new string('0', 8);
            for (int i = 0; i < 4; i++) line[75 + i] = debutText[i];
            for (int i = 0; i < 2; i++) line[79 + i] = debutText[4 + i];
            for (int i = 0; i < 2; i++) line[81 + i] = debutText[6 + i];

            var finText = row.DateFin.HasValue ? row.DateFin.Value.ToString("yyyyMMdd") : new string('0', 8);
            for (int i = 0; i < 4; i++) line[83 + i] = finText[i];
            for (int i = 0; i < 2; i++) line[87 + i] = finText[4 + i];
            for (int i = 0; i < 2; i++) line[89 + i] = finText[6 + i];

            var ttcMillimesRow = (long)Math.Round(row.TotalTTC * 1000m, MidpointRounding.AwayFromZero);
            var ttcTextRow = ttcMillimesRow.ToString().PadLeft(10, '0');
            for (int i = 0; i < 10; i++) line[87 + i] = ttcTextRow[i];

            var htMillimesRow = (long)Math.Round((row.TotalTTC / 1.07m) * 1000m, MidpointRounding.AwayFromZero);
            var htTextRow = htMillimesRow.ToString().PadLeft(10, '0');
            for (int i = 0; i < 10; i++) line[97 + i] = htTextRow[i];

            var tvaGeneral = "0000007";
            for (int i = 0; i < 7; i++) line[107 + i] = tvaGeneral[i];

            var tvaMillimesRow = (ttcMillimesRow - htMillimesRow).ToString().PadLeft(13, '0');
            for (int i = 0; i < 13; i++) line[114 + i] = tvaMillimesRow[i];

            var factureDate = (row.DateFacture ?? row.DateDebut ?? DateTime.Today).ToString("yyyyMMdd");
            for (int i = 0; i < 8; i++) line[127 + i] = factureDate[i];

            textBuilder.AppendLine(new string(line));
        }

        return Ok(textBuilder.ToString());

        static (string, string, string) SplitCabinetCode(string? codeCnam)
        {
            var parts = (codeCnam ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
            var first = NormalizeDigits(parts.ElementAtOrDefault(0), 2);
            var second = NormalizeDigits(parts.ElementAtOrDefault(1), 8);
            var third = NormalizeDigits(parts.ElementAtOrDefault(2), 2);
            return (first, second, third);
        }

        static (string, string) SplitEmployerNumber(string? employerNumber)
        {
            var parts = (employerNumber ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
            var first = NormalizeDigits(parts.ElementAtOrDefault(0), 11);
            var second = NormalizeDigits(parts.ElementAtOrDefault(1), 2);
            return (first, second);
        }

        static string NormalizeDigits(string? raw, int width)
        {
            var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length > width)
            {
                digits = digits[^width..];
            }
            return digits.PadLeft(width, '0');
        }

        static string FormatFactureNumber(string? factureNumber)
        {
            if (string.IsNullOrWhiteSpace(factureNumber))
            {
                return new string(' ', 8);
            }

            var normalized = factureNumber.Trim();
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var sequence) && parts[1].Length == 4)
            {
                var invoiceText = $"{sequence:000}/{parts[1]}";
                return invoiceText.Length <= 8 ? invoiceText.PadRight(8, ' ') : invoiceText[..8];
            }

            return normalized.Length <= 8 ? normalized.PadRight(8, ' ') : normalized[..8];
        }

        static (string, string) SplitDecisionParts(string? numeroDecision, int defaultYear)
        {
            if (string.IsNullOrWhiteSpace(numeroDecision))
            {
                return (defaultYear.ToString("0000"), new string('0', 6));
            }

            var parts = numeroDecision.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string yearPart = defaultYear.ToString("0000");
            string keyPart = new string('0', 6);

            if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out var yearValue))
            {
                yearPart = yearValue.ToString("0000");
            }

            if (parts.Length >= 2)
            {
                var keyDigits = new string(parts[1].Where(char.IsDigit).ToArray());
                if (keyDigits.Length > 6)
                {
                    keyDigits = keyDigits[^6..];
                }
                keyPart = keyDigits.PadLeft(6, '0');
            }
            else
            {
                var digits = new string(numeroDecision.Where(char.IsDigit).ToArray());
                if (digits.Length >= 10)
                {
                    yearPart = digits[..4];
                    keyPart = digits[4..10];
                }
                else if (digits.Length > 4)
                {
                    keyPart = digits[4..].PadLeft(6, '0');
                }
            }

            return (yearPart, keyPart);
        }
    }

    [HttpGet("advance-lots/patient/{patientId}")]
    public ActionResult<IEnumerable<AdvanceLotDto>> GetAdvanceLots(int patientId)
    {
        var lots = new List<AdvanceLotDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, transaction_id, COALESCE(original_amount, 0), COALESCE(remaining_amount, 0), COALESCE(created_at, NOW())
            FROM advance_lots
            WHERE patient_id = @patientId
            ORDER BY created_at DESC
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            lots.Add(new AdvanceLotDto
            {
                Id = reader.GetInt32(0),
                TransactionId = reader.GetInt32(1),
                OriginalAmount = reader.GetDecimal(2),
                RemainingAmount = reader.GetDecimal(3),
                CreatedAt = reader.GetDateTime(4)
            });
        }

        return Ok(lots);
    }

    [HttpGet("patient-finance/{patientId}")]
    public ActionResult<PatientFinanceDto> GetPatientFinance(int patientId)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(advance_balance, 0), COALESCE(total_advance_paid, 0)
            FROM patient_finance
            WHERE patient_id = @patientId
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return Ok(new PatientFinanceDto
            {
                PatientId = patientId,
                AdvanceBalance = reader.GetDecimal(0),
                TotalAdvancePaid = reader.GetDecimal(1)
            });
        }

        reader.Close();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(remaining_amount), 0), 0
            FROM advance_lots
            WHERE patient_id = @patientId
        ";
        using var reader2 = cmd.ExecuteReader();
        if (reader2.Read())
        {
            return Ok(new PatientFinanceDto
            {
                PatientId = patientId,
                AdvanceBalance = reader2.GetDecimal(0),
                TotalAdvancePaid = 0
            });
        }

        return Ok(new PatientFinanceDto
        {
            PatientId = patientId,
            AdvanceBalance = 0,
            TotalAdvancePaid = 0
        });
    }

    [HttpGet("patient-summary/{patientId}")]
    public ActionResult<PatientFinancialSummaryDto> GetPatientFinancialSummary(int patientId)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        decimal totalAmountDue = 0;
        decimal totalPaid = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COALESCE(SUM(amount), 0), COALESCE(SUM(paid_amount), 0)
                FROM appointments
                WHERE patient_id = @patientId
            ";
            cmd.Parameters.AddWithValue("@patientId", patientId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                totalAmountDue = reader.GetDecimal(0);
                totalPaid = reader.GetDecimal(1);
            }
        }

        decimal advanceBalance = 0;
        decimal totalAdvancePaid = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COALESCE(advance_balance, 0), COALESCE(total_advance_paid, 0)
                FROM patient_finance
                WHERE patient_id = @patientId
            ";
            cmd.Parameters.AddWithValue("@patientId", patientId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                advanceBalance = reader.GetDecimal(0);
                totalAdvancePaid = reader.GetDecimal(1);
            }
        }

        if (advanceBalance == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(remaining_amount), 0)
                FROM advance_lots
                WHERE patient_id = @patientId
            ";
            cmd.Parameters.AddWithValue("@patientId", patientId);

            var result = Convert.ToDecimal(cmd.ExecuteScalar());
            advanceBalance = result;
        }

        var outstanding = totalAmountDue - totalPaid;
        var outstandingAfterAdvance = Math.Max(0, outstanding - advanceBalance);

        return Ok(new PatientFinancialSummaryDto
        {
            PatientId = patientId,
            TotalAmountDue = totalAmountDue,
            TotalPaid = totalPaid,
            OutstandingAmount = outstanding,
            AdvanceBalance = advanceBalance,
            TotalAdvancePaid = totalAdvancePaid,
            OutstandingAfterAdvance = outstandingAfterAdvance
        });
    }

    [HttpPost("advance-transactions")]
    public ActionResult<AdvanceTransactionDto> CreateAdvanceTransaction(AdvanceTransactionRequestDto request)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO advance_transactions(patient_id, amount, transaction_date, note, created_by)
            VALUES (@patientId, @amount, @transactionDate, @note, @createdBy)
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@patientId", request.PatientId);
        cmd.Parameters.AddWithValue("@amount", request.Amount);
        cmd.Parameters.AddWithValue("@transactionDate", request.TransactionDate == default ? DateTime.UtcNow : request.TransactionDate);
        cmd.Parameters.AddWithValue("@note", (object?)request.Note ?? string.Empty);
        cmd.Parameters.AddWithValue("@createdBy", (object?)request.CreatedBy ?? "Web");

        var transactionId = Convert.ToInt32(cmd.ExecuteScalar());

        using var lotCmd = conn.CreateCommand();
        lotCmd.CommandText = @"
            INSERT INTO advance_lots(patient_id, transaction_id, original_amount, remaining_amount)
            VALUES (@patientId, @transactionId, @amount, @amount)
        ";
        lotCmd.Parameters.AddWithValue("@patientId", request.PatientId);
        lotCmd.Parameters.AddWithValue("@transactionId", transactionId);
        lotCmd.Parameters.AddWithValue("@amount", request.Amount);
        lotCmd.ExecuteNonQuery();

        using var financeCmd = conn.CreateCommand();
        financeCmd.CommandText = @"
            INSERT INTO patient_finance(patient_id, session_price, patient_share, cnam_share, advance_balance, total_advance_paid)
            VALUES (@patientId, 0, 0, 0, @amount, 0)
            ON CONFLICT (patient_id) DO UPDATE
            SET advance_balance = patient_finance.advance_balance + EXCLUDED.advance_balance
        ";
        financeCmd.Parameters.AddWithValue("@patientId", request.PatientId);
        financeCmd.Parameters.AddWithValue("@amount", request.Amount);
        financeCmd.ExecuteNonQuery();

        using var ledgerCmd = conn.CreateCommand();
        ledgerCmd.CommandText = @"
            INSERT INTO finance_ledger(patient_id, appointment_id, entry_type, amount, reference, note)
            VALUES (@patientId, NULL, 'credit_avance', @amount, @reference, @note)
        ";
        ledgerCmd.Parameters.AddWithValue("@patientId", request.PatientId);
        ledgerCmd.Parameters.AddWithValue("@amount", request.Amount);
        ledgerCmd.Parameters.AddWithValue("@reference", transactionId.ToString());
        ledgerCmd.Parameters.AddWithValue("@note", (object?)request.Note ?? string.Empty);
        ledgerCmd.ExecuteNonQuery();

        return CreatedAtAction(nameof(GetAdvanceTransactions), new { patientId = request.PatientId }, new AdvanceTransactionDto
        {
            Id = transactionId,
            PatientId = request.PatientId,
            Amount = request.Amount,
            TransactionDate = request.TransactionDate == default ? DateTime.UtcNow : request.TransactionDate,
            Note = request.Note,
            CreatedBy = request.CreatedBy ?? "Web"
        });
    }

    [HttpGet("advance-transactions/patient/{patientId}")]
    public ActionResult<IEnumerable<AdvanceTransactionDto>> GetAdvanceTransactions(int patientId)
    {
        if (!IsAdmin() && !IsPatientAccessible(patientId))
        {
            return Forbid();
        }

        var transactions = new List<AdvanceTransactionDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.id, t.patient_id, COALESCE(p.nom || ' ' || p.prenom, ''), t.amount, t.transaction_date, COALESCE(t.note, ''), COALESCE(t.created_by, '')
            FROM advance_transactions t
            JOIN patients p ON p.id = t.patient_id
            WHERE t.patient_id = @patientId
            ORDER BY t.transaction_date DESC
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            transactions.Add(new AdvanceTransactionDto
            {
                Id = reader.GetInt32(0),
                PatientId = reader.GetInt32(1),
                PatientName = reader.GetString(2),
                Amount = reader.GetDecimal(3),
                TransactionDate = reader.GetDateTime(4),
                Note = reader.GetString(5),
                CreatedBy = reader.GetString(6)
            });
        }

        return Ok(transactions);
    }

    [HttpGet("advance-transactions")]
    public ActionResult<IEnumerable<AdvanceTransactionDto>> GetAllAdvanceTransactions()
    {
        if (!IsAdmin())
        {
            return Forbid();
        }

        var transactions = new List<AdvanceTransactionDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.id, t.patient_id, COALESCE(p.nom || ' ' || p.prenom, ''), t.amount, t.transaction_date, COALESCE(t.note, ''), COALESCE(t.created_by, '')
            FROM advance_transactions t
            JOIN patients p ON p.id = t.patient_id
            ORDER BY t.transaction_date DESC
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            transactions.Add(new AdvanceTransactionDto
            {
                Id = reader.GetInt32(0),
                PatientId = reader.GetInt32(1),
                PatientName = reader.GetString(2),
                Amount = reader.GetDecimal(3),
                TransactionDate = reader.GetDateTime(4),
                Note = reader.GetString(5),
                CreatedBy = reader.GetString(6)
            });
        }

        return Ok(transactions);
    }

    [HttpDelete("advance-transactions/{transactionId}")]
    public IActionResult DeleteAdvanceTransaction(int transactionId)
    {
        if (!IsAdmin() && !IsAdvanceTransactionAccessible(transactionId))
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM advance_transactions
            WHERE id = @transactionId
        ";
        cmd.Parameters.AddWithValue("@transactionId", transactionId);

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpPut("advance-transactions/{transactionId}")]
    public IActionResult UpdateAdvanceTransaction(int transactionId, AdvanceTransactionRequestDto request)
    {
        if (!IsAdmin() && !IsAdvanceTransactionAccessible(transactionId))
        {
            return Forbid();
        }

        if (!IsAdmin() && !IsPatientAccessible(request.PatientId))
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE advance_transactions
            SET amount = @amount,
                note = @note,
                transaction_date = @transactionDate,
                created_by = @createdBy
            WHERE id = @transactionId
        ";
        cmd.Parameters.AddWithValue("@amount", request.Amount);
        cmd.Parameters.AddWithValue("@note", (object?)request.Note ?? string.Empty);
        cmd.Parameters.AddWithValue("@transactionDate", request.TransactionDate == default ? DateTime.UtcNow : request.TransactionDate);
        cmd.Parameters.AddWithValue("@createdBy", (object?)request.CreatedBy ?? "Web");
        cmd.Parameters.AddWithValue("@transactionId", transactionId);

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpGet("payment-projections/patient/{patientId}")]
    public ActionResult<IEnumerable<PaymentProjectionEntryDto>> GetPaymentProjections(int patientId)
    {
        decimal availableAdvance;
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COALESCE(advance_balance, 0)
                FROM patient_finance
                WHERE patient_id = @patientId
            ";
            cmd.Parameters.AddWithValue("@patientId", patientId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                availableAdvance = reader.GetDecimal(0);
            }
            else
            {
                reader.Close();
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(remaining_amount), 0)
                    FROM advance_lots
                    WHERE patient_id = @patientId
                ";
                using var reader2 = cmd.ExecuteReader();
                availableAdvance = reader2.Read() ? reader2.GetDecimal(0) : 0m;
            }
        }

        var projections = new List<PaymentProjectionEntryDto>();
        using var projectionCmd = conn.CreateCommand();
        projectionCmd.CommandText = @"
            SELECT id, COALESCE(amount, 0), COALESCE(paid_amount, 0), COALESCE(payment_status, 'non_paye')
            FROM appointments
            WHERE patient_id = @patientId
            ORDER BY start_datetime, id
        ";
        projectionCmd.Parameters.AddWithValue("@patientId", patientId);

        using var projectionReader = projectionCmd.ExecuteReader();
        while (projectionReader.Read())
        {
            var appointmentId = projectionReader.GetInt32(0);
            var amountDue = projectionReader.GetDecimal(1);
            var paidTotal = projectionReader.GetDecimal(2);
            var currentStatus = projectionReader.GetString(3);
            var remaining = Math.Max(0, amountDue - paidTotal);
            var projectedUse = 0m;
            if (remaining > 0 && availableAdvance > 0)
            {
                projectedUse = Math.Min(availableAdvance, remaining);
                availableAdvance -= projectedUse;
            }
            var projectedPaid = paidTotal + projectedUse;
            projections.Add(new PaymentProjectionEntryDto
            {
                AppointmentId = appointmentId,
                PaidTotal = projectedPaid,
                PaymentStatus = ComputeStatus(amountDue, projectedPaid),
                ProjectedAdvance = projectedUse,
                HasProjection = projectedUse > 0,
                SourceStatus = currentStatus
            });
        }

        return Ok(projections);
    }

    private decimal ComputeExpectedAmount(DateTime startDate, DateTime endDate)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(a.paid_amount - COALESCE(au.amount_used, 0)), 0)
            FROM appointments a
            LEFT JOIN advance_usage au ON au.appointment_id = a.id
            WHERE DATE(a.start_datetime) BETWEEN @start AND @end
              AND a.status IN ('present', 'effectue')
        ";
        cmd.Parameters.AddWithValue("@start", startDate.Date);
        cmd.Parameters.AddWithValue("@end", endDate.Date);

        var sessionsCash = Convert.ToDecimal(cmd.ExecuteScalar());

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = @"
            SELECT COALESCE(SUM(amount), 0)
            FROM advance_transactions
            WHERE DATE(transaction_date) BETWEEN @start AND @end
        ";
        cmd2.Parameters.AddWithValue("@start", startDate.Date);
        cmd2.Parameters.AddWithValue("@end", endDate.Date);

        var advancesCash = Convert.ToDecimal(cmd2.ExecuteScalar());
        return sessionsCash + advancesCash;
    }

    private static string ComputeStatus(decimal amountDue, decimal paidTotal)
    {
        if (amountDue <= 0)
        {
            return "non_paye";
        }

        if (paidTotal >= amountDue)
        {
            return "paye";
        }

        if (paidTotal > 0)
        {
            return "partiel";
        }

        return "non_paye";
    }
}
