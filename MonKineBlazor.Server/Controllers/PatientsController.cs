using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Server.Services;
using MonKineBlazor.Shared.Models;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private UserDto? GetCurrentUser() => UserContextHelper.GetCurrentUser(HttpContext);
    private bool IsAdmin() => UserContextHelper.IsAdmin(HttpContext);
    private bool IsPatientAccessible(int patientId) => UserContextHelper.IsPatientAccessible(HttpContext, patientId);

    private static string ComputeNumeroAssuree(string? racine, string? cle, string? numeroAssuree)
    {
        var cleanRacine = string.IsNullOrWhiteSpace(racine) ? string.Empty : racine.Trim();
        var cleanCle = string.IsNullOrWhiteSpace(cle) ? string.Empty : cle.Trim();

        if (!string.IsNullOrEmpty(cleanRacine) && !string.IsNullOrEmpty(cleanCle))
        {
            return $"{cleanRacine}/{cleanCle}";
        }

        return string.IsNullOrWhiteSpace(numeroAssuree) ? string.Empty : numeroAssuree.Trim();
    }

    private static string NormalizeKeyValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static bool PatientExists(NpgsqlConnection connection, int? cabinetId, string? racine, string? cle, string? qualite, int? excludeId = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM patients
            WHERE ((@cabinet_id IS NULL AND cabinet_id IS NULL) OR cabinet_id = @cabinet_id)
              AND lower(trim(coalesce(racine, ''))) = @racine
              AND lower(trim(coalesce(cle, ''))) = @cle
              AND lower(trim(coalesce(qualite, ''))) = @qualite
        ";
        if (excludeId.HasValue)
        {
            cmd.CommandText += "\n            AND id <> @excludeId";
            cmd.Parameters.AddWithValue("@excludeId", excludeId.Value);
        }

        cmd.Parameters.AddWithValue("@cabinet_id", cabinetId.HasValue ? (object)cabinetId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@racine", NormalizeKeyValue(racine));
        cmd.Parameters.AddWithValue("@cle", NormalizeKeyValue(cle));
        cmd.Parameters.AddWithValue("@qualite", NormalizeKeyValue(qualite));

        return cmd.ExecuteScalar() != null;
    }

    [HttpGet]
    public ActionResult<IEnumerable<PatientDto>> GetAll()
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var patients = new List<PatientDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (IsAdmin())
        {
            cmd.CommandText = @"
                SELECT
                    id,
                    cabinet_id,
                    COALESCE(code_patient, ''),
                    COALESCE(dossier_patient, ''),
                    nom,
                    COALESCE(prenom, ''),
                    CASE
                        WHEN date_naissance IS NOT NULL
                            THEN CAST(DATE_PART('year', AGE(CURRENT_DATE, date_naissance)) AS INTEGER)
                        ELSE COALESCE(age, 0)
                    END AS age,
                    date_naissance,
                    COALESCE(sexe, ''),
                    COALESCE(telephone1, ''),
                    COALESCE(telephone2, ''),
                    COALESCE(adresse, ''),
                    COALESCE(couverture, ''),
                    COALESCE(racine, ''),
                    COALESCE(cle, ''),
                    COALESCE(qualite, ''),
                    COALESCE(n_assuree, '')
                FROM patients
                ORDER BY nom, prenom
            ";
        }
        else
        {
            cmd.CommandText = @"
                SELECT
                    id,
                    cabinet_id,
                    COALESCE(code_patient, ''),
                    COALESCE(dossier_patient, ''),
                    nom,
                    COALESCE(prenom, ''),
                    CASE
                        WHEN date_naissance IS NOT NULL
                            THEN CAST(DATE_PART('year', AGE(CURRENT_DATE, date_naissance)) AS INTEGER)
                        ELSE COALESCE(age, 0)
                    END AS age,
                    date_naissance,
                    COALESCE(sexe, ''),
                    COALESCE(telephone1, ''),
                    COALESCE(telephone2, ''),
                    COALESCE(adresse, ''),
                    COALESCE(couverture, ''),
                    COALESCE(racine, ''),
                    COALESCE(cle, ''),
                    COALESCE(qualite, ''),
                    COALESCE(n_assuree, '')
                FROM patients
                WHERE cabinet_id = @cabinet_id
                ORDER BY nom, prenom
            ";
            cmd.Parameters.AddWithValue("@cabinet_id", currentUser.CabinetId.HasValue ? (object)currentUser.CabinetId.Value : DBNull.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            patients.Add(new PatientDto
            {
                Id = reader.GetInt32(0),
                CabinetId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                CodePatient = reader.GetString(2),
                DossierPatient = reader.GetString(3),
                Nom = reader.GetString(4),
                Prenom = reader.GetString(5),
                Age = reader.GetInt32(6),
                DateNaissance = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                Sexe = reader.GetString(8),
                Telephone1 = reader.GetString(9),
                Telephone2 = reader.GetString(10),
                Adresse = reader.GetString(11),
                Couverture = reader.GetString(12),
                Racine = reader.GetString(13),
                Cle = reader.GetString(14),
                Qualite = reader.GetString(15),
                NumeroAssuree = ComputeNumeroAssuree(reader.GetString(13), reader.GetString(14), reader.GetString(16)),
            });
        }

        return Ok(patients);
    }

    [HttpGet("{id}")]
    public ActionResult<PatientDto> GetById(int id)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                id,
                cabinet_id,
                COALESCE(code_patient, ''),
                COALESCE(dossier_patient, ''),
                nom,
                COALESCE(prenom, ''),
                CASE
                    WHEN date_naissance IS NOT NULL
                        THEN CAST(DATE_PART('year', AGE(CURRENT_DATE, date_naissance)) AS INTEGER)
                    ELSE COALESCE(age, 0)
                END AS age,
                date_naissance,
                COALESCE(sexe, ''),
                COALESCE(telephone1, ''),
                COALESCE(telephone2, ''),
                COALESCE(adresse, ''),
                COALESCE(couverture, ''),
                COALESCE(racine, ''),
                COALESCE(cle, ''),
                COALESCE(qualite, ''),
                COALESCE(n_assuree, '')
            FROM patients
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return NotFound();
        }

        int? cabinetId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
        if (!IsAdmin() && cabinetId != currentUser.CabinetId)
        {
            return Forbid();
        }

        var patient = new PatientDto
        {
            Id = reader.GetInt32(0),
            CabinetId = cabinetId,
            CodePatient = reader.GetString(2),
            DossierPatient = reader.GetString(3),
            Nom = reader.GetString(4),
            Prenom = reader.GetString(5),
            Age = reader.GetInt32(6),
            DateNaissance = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            Sexe = reader.GetString(8),
            Telephone1 = reader.GetString(9),
            Telephone2 = reader.GetString(10),
            Adresse = reader.GetString(11),
            Couverture = reader.GetString(12),
            Racine = reader.GetString(13),
            Cle = reader.GetString(14),
            Qualite = reader.GetString(15),
            NumeroAssuree = ComputeNumeroAssuree(reader.GetString(13), reader.GetString(14), reader.GetString(16)),
        };

        return Ok(patient);
    }

    [HttpGet("search")]
    public ActionResult<IEnumerable<PatientDto>> Search([FromQuery] string? racine, [FromQuery] string? cle, [FromQuery] string? qualite)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(racine) || string.IsNullOrWhiteSpace(cle) || string.IsNullOrWhiteSpace(qualite))
        {
            return BadRequest("Racine, clé et qualité sont requis pour la recherche de patient.");
        }

        var normalizedRacine = racine.Trim().ToLowerInvariant();
        var normalizedCle = cle.Trim().ToLowerInvariant();
        var normalizedQualite = qualite.Trim().ToLowerInvariant();

        var patients = new List<PatientDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        if (IsAdmin())
        {
            cmd.CommandText = @"
                SELECT
                    id,
                    cabinet_id,
                    COALESCE(code_patient, ''),
                    COALESCE(dossier_patient, ''),
                    nom,
                    COALESCE(prenom, ''),
                    CASE
                        WHEN date_naissance IS NOT NULL
                            THEN CAST(DATE_PART('year', AGE(CURRENT_DATE, date_naissance)) AS INTEGER)
                        ELSE COALESCE(age, 0)
                    END AS age,
                    date_naissance,
                    COALESCE(sexe, ''),
                    COALESCE(telephone1, ''),
                    COALESCE(telephone2, ''),
                    COALESCE(adresse, ''),
                    COALESCE(couverture, ''),
                    COALESCE(racine, ''),
                    COALESCE(cle, ''),
                    COALESCE(qualite, ''),
                    COALESCE(n_assuree, '')
                FROM patients
                WHERE lower(trim(COALESCE(racine, ''))) = @racine
                  AND lower(trim(COALESCE(cle, ''))) = @cle
                  AND lower(trim(COALESCE(qualite, ''))) = @qualite
                ORDER BY nom, prenom
            ";
        }
        else
        {
            cmd.CommandText = @"
                SELECT
                    id,
                    cabinet_id,
                    COALESCE(code_patient, ''),
                    COALESCE(dossier_patient, ''),
                    nom,
                    COALESCE(prenom, ''),
                    CASE
                        WHEN date_naissance IS NOT NULL
                            THEN CAST(DATE_PART('year', AGE(CURRENT_DATE, date_naissance)) AS INTEGER)
                        ELSE COALESCE(age, 0)
                    END AS age,
                    date_naissance,
                    COALESCE(sexe, ''),
                    COALESCE(telephone1, ''),
                    COALESCE(telephone2, ''),
                    COALESCE(adresse, ''),
                    COALESCE(couverture, ''),
                    COALESCE(racine, ''),
                    COALESCE(cle, ''),
                    COALESCE(qualite, ''),
                    COALESCE(n_assuree, '')
                FROM patients
                WHERE cabinet_id = @cabinet_id
                  AND lower(trim(COALESCE(racine, ''))) = @racine
                  AND lower(trim(COALESCE(cle, ''))) = @cle
                  AND lower(trim(COALESCE(qualite, ''))) = @qualite
                ORDER BY nom, prenom
            ";
            cmd.Parameters.AddWithValue("@cabinet_id", currentUser.CabinetId.HasValue ? (object)currentUser.CabinetId.Value : DBNull.Value);
        }

        cmd.Parameters.AddWithValue("@racine", normalizedRacine);
        cmd.Parameters.AddWithValue("@cle", normalizedCle);
        cmd.Parameters.AddWithValue("@qualite", normalizedQualite);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            patients.Add(new PatientDto
            {
                Id = reader.GetInt32(0),
                CabinetId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                CodePatient = reader.GetString(2),
                DossierPatient = reader.GetString(3),
                Nom = reader.GetString(4),
                Prenom = reader.GetString(5),
                Age = reader.GetInt32(6),
                DateNaissance = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                Sexe = reader.GetString(8),
                Telephone1 = reader.GetString(9),
                Telephone2 = reader.GetString(10),
                Adresse = reader.GetString(11),
                Couverture = reader.GetString(12),
                Racine = reader.GetString(13),
                Cle = reader.GetString(14),
                Qualite = reader.GetString(15),
                NumeroAssuree = ComputeNumeroAssuree(reader.GetString(13), reader.GetString(14), reader.GetString(16)),
            });
        }

        return Ok(patients);
    }

    [HttpPost]
    public ActionResult<PatientDto> Create(PatientDto patient)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        if (!IsAdmin())
        {
            if (currentUser.CabinetId == null)
            {
                return Forbid();
            }
            if (patient.CabinetId.HasValue && patient.CabinetId != currentUser.CabinetId)
            {
                return Forbid();
            }
            patient.CabinetId = currentUser.CabinetId;
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        if (PatientExists(conn, patient.CabinetId, patient.Racine, patient.Cle, patient.Qualite))
        {
            return Conflict("Un patient CNAM avec cette racine, clé et qualité existe déjà.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO patients (
                cabinet_id, code_patient, dossier_patient, nom, prenom, age, date_naissance,
                sexe, telephone1, telephone2, adresse, couverture, racine, cle, qualite, n_assuree
            ) VALUES (
                @cabinet_id, @code_patient, @dossier_patient, @nom, @prenom, @age, @date_naissance,
                @sexe, @telephone1, @telephone2, @adresse, @couverture, @racine, @cle, @qualite, @n_assuree
            )
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@cabinet_id", (object?)patient.CabinetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_patient", (object?)patient.CodePatient ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dossier_patient", (object?)patient.DossierPatient ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom", patient.Nom ?? string.Empty);
        cmd.Parameters.AddWithValue("@prenom", (object?)patient.Prenom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@age", patient.Age);
        cmd.Parameters.AddWithValue("@date_naissance", (object?)patient.DateNaissance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sexe", (object?)patient.Sexe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telephone1", (object?)patient.Telephone1 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telephone2", (object?)patient.Telephone2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@adresse", (object?)patient.Adresse ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@couverture", (object?)patient.Couverture ?? DBNull.Value);
        var nAssureeValue = ComputeNumeroAssuree(patient.Racine, patient.Cle, patient.NumeroAssuree);
        cmd.Parameters.AddWithValue("@racine", (object?)patient.Racine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cle", (object?)patient.Cle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qualite", (object?)patient.Qualite ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n_assuree", (object?)nAssureeValue ?? DBNull.Value);

        var createdId = Convert.ToInt32(cmd.ExecuteScalar());
        patient.Id = createdId;
        patient.NumeroAssuree = nAssureeValue;

        return CreatedAtAction(nameof(GetById), new { id = createdId }, patient);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, PatientDto patient)
    {
        if (id != patient.Id)
        {
            return BadRequest("Le patient ID ne correspond pas.");
        }

        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        if (!IsAdmin())
        {
            if (currentUser.CabinetId == null)
            {
                return Forbid();
            }
            if (patient.CabinetId.HasValue && patient.CabinetId != currentUser.CabinetId)
            {
                return Forbid();
            }
            patient.CabinetId = currentUser.CabinetId;
        }

        if (!IsAdmin() && !IsPatientAccessible(id))
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        if (PatientExists(conn, patient.CabinetId, patient.Racine, patient.Cle, patient.Qualite, id))
        {
            return Conflict("Un patient CNAM avec cette racine, clé et qualité existe déjà.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE patients
            SET
                cabinet_id = @cabinet_id,
                code_patient = @code_patient,
                dossier_patient = @dossier_patient,
                nom = @nom,
                prenom = @prenom,
                age = @age,
                date_naissance = @date_naissance,
                sexe = @sexe,
                telephone1 = @telephone1,
                telephone2 = @telephone2,
                adresse = @adresse,
                couverture = @couverture,
                racine = @racine,
                cle = @cle,
                qualite = @qualite,
                n_assuree = @n_assuree
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@cabinet_id", (object?)patient.CabinetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_patient", (object?)patient.CodePatient ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dossier_patient", (object?)patient.DossierPatient ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom", patient.Nom ?? string.Empty);
        cmd.Parameters.AddWithValue("@prenom", (object?)patient.Prenom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@age", patient.Age);
        cmd.Parameters.AddWithValue("@date_naissance", (object?)patient.DateNaissance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sexe", (object?)patient.Sexe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telephone1", (object?)patient.Telephone1 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telephone2", (object?)patient.Telephone2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@adresse", (object?)patient.Adresse ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@couverture", (object?)patient.Couverture ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@racine", (object?)patient.Racine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cle", (object?)patient.Cle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qualite", (object?)patient.Qualite ?? DBNull.Value);
        var nAssureeValue = ComputeNumeroAssuree(patient.Racine, patient.Cle, patient.NumeroAssuree);
        cmd.Parameters.AddWithValue("@n_assuree", (object?)nAssureeValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);

        var rowsUpdated = cmd.ExecuteNonQuery();
        if (rowsUpdated == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}
