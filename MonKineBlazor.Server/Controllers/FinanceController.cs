using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Shared.Models;
using Npgsql;
using System.Linq;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FinanceController : ControllerBase
{
    [HttpGet("cash-closings")]
    public ActionResult<IEnumerable<CashClosingDto>> GetCashClosings(DateTime start, DateTime end)
    {
        var sessionsByDay = new Dictionary<DateTime, decimal>();
        var advancesByDay = new Dictionary<DateTime, decimal>();
        var closingsByDay = new Dictionary<DateTime, CashClosingDto>();

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DATE(a.start_datetime), COALESCE(SUM(a.paid_amount - COALESCE(au.amount_used, 0)), 0)
                FROM appointments a
                LEFT JOIN advance_usage au ON au.appointment_id = a.id
                WHERE DATE(a.start_datetime) BETWEEN @start AND @end
                  AND a.status IN ('present', 'effectue')
                GROUP BY DATE(a.start_datetime)
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sessionsByDay[reader.GetDateTime(0).Date] = reader.GetDecimal(1);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DATE(transaction_date), COALESCE(SUM(amount), 0)
                FROM advance_transactions
                WHERE DATE(transaction_date) BETWEEN @start AND @end
                GROUP BY DATE(transaction_date)
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);

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
        var rows = new List<CnamRecoveryDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
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
        ";
        cmd.Parameters.AddWithValue("@start", start.Date);
        cmd.Parameters.AddWithValue("@end", end.Date);

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
            var programs = new List<CnamProgramInvoiceDto>();
            using var conn = DatabaseConnectionProvider.CreateConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
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
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);

            using var reader = cmd.ExecuteReader();
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
                    Titre = reader.GetString(6),
                    NatureSeances = reader.GetString(7),
                    NbSeances = reader.GetInt32(8),
                    DureeSeanceMinutes = reader.GetInt32(9),
                    DateDebut = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    DateFin = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    PrixUnitaire = reader.GetDecimal(12),
                    PrixTTC = reader.GetDecimal(13),
                    CodeBureau = reader.GetString(14),
                    Annee = reader.GetString(15),
                    NumeroDecision = reader.GetString(16),
                    NumeroOrdre = reader.GetString(17)
                });
            }

            return Ok(programs);
        }

        [HttpGet("cnam-bordereau")]
        public ActionResult<IEnumerable<CnamBordereauEntryDto>> GetCnamBordereau(DateTime start, DateTime end)
        {
            var rows = new List<CnamBordereauEntryDto>();
            using var conn = DatabaseConnectionProvider.CreateConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    pp.id,
                    pp.date_debut,
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(pp.prix_ttc, 0)
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                WHERE DATE(pp.date_debut) BETWEEN @start AND @end
                  AND COALESCE(p.couverture, '') <> ''
                  AND COALESCE(p.n_assuree, '') <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM cnam_bordereau_executed e WHERE e.program_id = pp.id
                  )
                ORDER BY pp.date_debut
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var programId = reader.GetInt32(0);
                var dateDebut = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                rows.Add(new CnamBordereauEntryDto
                {
                    ProgramId = programId,
                    FactureNumber = dateDebut.HasValue ? $"INV-{programId}-{dateDebut:yyyyMMdd}" : $"INV-{programId}",
                    DateFacture = dateDebut,
                    CodePatient = reader.GetString(2),
                    NumeroAssuree = reader.GetString(3),
                    PatientName = reader.GetString(4),
                    TotalTTC = reader.GetDecimal(5)
                });
            }

            return Ok(rows);
        }

        [HttpGet("cnam-bordereau-executed")]
        public ActionResult<IEnumerable<CnamBordereauEntryDto>> GetCnamBordereauExecuted(DateTime start, DateTime end)
        {
            var rows = new List<CnamBordereauEntryDto>();
            using var conn = DatabaseConnectionProvider.CreateConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    pp.id,
                    pp.date_debut,
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(pp.prix_ttc, 0),
                    e.executed_at,
                    COALESCE(e.executed_by, '')
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                JOIN cnam_bordereau_executed e ON e.program_id = pp.id
                WHERE DATE(pp.date_debut) BETWEEN @start AND @end
                  AND COALESCE(p.couverture, '') <> ''
                  AND COALESCE(p.n_assuree, '') <> ''
                ORDER BY e.executed_at DESC
            ";
            cmd.Parameters.AddWithValue("@start", start.Date);
            cmd.Parameters.AddWithValue("@end", end.Date);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var programId = reader.GetInt32(0);
                var dateDebut = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                rows.Add(new CnamBordereauEntryDto
                {
                    ProgramId = programId,
                    FactureNumber = dateDebut.HasValue ? $"INV-{programId}-{dateDebut:yyyyMMdd}" : $"INV-{programId}",
                    DateFacture = dateDebut,
                    CodePatient = reader.GetString(2),
                    NumeroAssuree = reader.GetString(3),
                    PatientName = reader.GetString(4),
                    TotalTTC = reader.GetDecimal(5),
                    ExecutedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    ExecutedBy = reader.GetString(7)
                });
            }

            return Ok(rows);
        }

        [HttpPost("cnam-bordereau/execute")]
        public ActionResult<CnamBordereauEntryDto> ExecuteCnamBordereau(CnamBordereauExecuteRequestDto request)
        {
            using var conn = DatabaseConnectionProvider.CreateConnection();
            conn.Open();

            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = @"
                SELECT pp.id
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                WHERE pp.id = @programId
                  AND COALESCE(p.couverture, '') <> ''
                  AND COALESCE(p.n_assuree, '') <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM cnam_bordereau_executed e WHERE e.program_id = pp.id
                  )
            ";
            checkCmd.Parameters.AddWithValue("@programId", request.ProgramId);
            var programIdObj = checkCmd.ExecuteScalar();
            if (programIdObj == null)
            {
                return BadRequest("Programme introuvable ou déjà exécuté.");
            }

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO cnam_bordereau_executed(program_id, executed_at, executed_by)
                VALUES (@programId, NOW(), @executedBy)
                ON CONFLICT (program_id) DO NOTHING
            ";
            insertCmd.Parameters.AddWithValue("@programId", request.ProgramId);
            insertCmd.Parameters.AddWithValue("@executedBy", request.ExecutedBy ?? "Web");
            insertCmd.ExecuteNonQuery();

            using var fetchCmd = conn.CreateCommand();
            fetchCmd.CommandText = @"
                SELECT
                    pp.id,
                    pp.date_debut,
                    COALESCE(p.code_patient, ''),
                    COALESCE(p.n_assuree, ''),
                    COALESCE(p.nom || ' ' || p.prenom, ''),
                    COALESCE(pp.prix_ttc, 0),
                    e.executed_at,
                    COALESCE(e.executed_by, '')
                FROM patient_programs pp
                JOIN patients p ON p.id = pp.patient_id
                JOIN cnam_bordereau_executed e ON e.program_id = pp.id
                WHERE pp.id = @programId
            ";
            fetchCmd.Parameters.AddWithValue("@programId", request.ProgramId);

            using var reader = fetchCmd.ExecuteReader();
            if (!reader.Read())
            {
                return NotFound();
            }

            var dateDebut = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
            var executedEntry = new CnamBordereauEntryDto
            {
                ProgramId = reader.GetInt32(0),
                FactureNumber = dateDebut.HasValue ? $"INV-{reader.GetInt32(0)}-{dateDebut:yyyyMMdd}" : $"INV-{reader.GetInt32(0)}",
                DateFacture = dateDebut,
                CodePatient = reader.GetString(2),
                NumeroAssuree = reader.GetString(3),
                PatientName = reader.GetString(4),
                TotalTTC = reader.GetDecimal(5),
                ExecutedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                ExecutedBy = reader.GetString(7)
            };

            return Ok(executedEntry);
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
