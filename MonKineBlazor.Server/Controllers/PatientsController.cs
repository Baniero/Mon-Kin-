using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Shared.Models;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<PatientDto>> GetAll()
    {
        var patients = new List<PatientDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                id,
                COALESCE(code_patient, ''),
                COALESCE(dossier_patient, ''),
                nom,
                COALESCE(prenom, ''),
                CASE
                    WHEN date_naissance IS NOT NULL AND date_naissance <> ''
                        THEN CAST(DATE_PART('year', AGE(CURRENT_DATE, NULLIF(date_naissance, '')::date)) AS INTEGER)
                    ELSE COALESCE(age, 0)
                END AS age,
                date_naissance,
                COALESCE(sexe, ''),
                COALESCE(telephone1, ''),
                COALESCE(telephone2, ''),
                COALESCE(adresse, ''),
                COALESCE(couverture, '')
            FROM patients
            ORDER BY nom, prenom
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            patients.Add(new PatientDto
            {
                Id = reader.GetInt32(0),
                CodePatient = reader.GetString(1),
                DossierPatient = reader.GetString(2),
                Nom = reader.GetString(3),
                Prenom = reader.GetString(4),
                Age = reader.GetInt32(5),
                DateNaissance = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                Sexe = reader.GetString(7),
                Telephone1 = reader.GetString(8),
                Telephone2 = reader.GetString(9),
                Adresse = reader.GetString(10),
                Couverture = reader.GetString(11),
            });
        }

        return Ok(patients);
    }

    [HttpGet("{id}")]
    public ActionResult<PatientDto> GetById(int id)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                id,
                COALESCE(code_patient, ''),
                COALESCE(dossier_patient, ''),
                nom,
                COALESCE(prenom, ''),
                CASE
                    WHEN date_naissance IS NOT NULL AND date_naissance <> ''
                        THEN CAST(DATE_PART('year', AGE(CURRENT_DATE, NULLIF(date_naissance, '')::date)) AS INTEGER)
                    ELSE COALESCE(age, 0)
                END AS age,
                date_naissance,
                COALESCE(sexe, ''),
                COALESCE(telephone1, ''),
                COALESCE(telephone2, ''),
                COALESCE(adresse, ''),
                COALESCE(couverture, '')
            FROM patients
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return NotFound();
        }

        var patient = new PatientDto
        {
            Id = reader.GetInt32(0),
            CodePatient = reader.GetString(1),
            DossierPatient = reader.GetString(2),
            Nom = reader.GetString(3),
            Prenom = reader.GetString(4),
            Age = reader.GetInt32(5),
            DateNaissance = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            Sexe = reader.GetString(7),
            Telephone1 = reader.GetString(8),
            Telephone2 = reader.GetString(9),
            Adresse = reader.GetString(10),
            Couverture = reader.GetString(11),
        };

        return Ok(patient);
    }

    [HttpPost]
    public ActionResult<PatientDto> Create(PatientDto patient)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO patients (
                code_patient, dossier_patient, nom, prenom, age, date_naissance,
                sexe, telephone1, telephone2, adresse, couverture
            ) VALUES (
                @code_patient, @dossier_patient, @nom, @prenom, @age, @date_naissance,
                @sexe, @telephone1, @telephone2, @adresse, @couverture
            )
            RETURNING id
        ";
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

        var createdId = Convert.ToInt32(cmd.ExecuteScalar());
        patient.Id = createdId;

        return CreatedAtAction(nameof(GetById), new { id = createdId }, patient);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, PatientDto patient)
    {
        if (id != patient.Id)
        {
            return BadRequest("Le patient ID ne correspond pas.");
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE patients
            SET
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
                couverture = @couverture
            WHERE id = @id
        ";
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
        cmd.Parameters.AddWithValue("@id", id);

        var rowsUpdated = cmd.ExecuteNonQuery();
        if (rowsUpdated == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}
