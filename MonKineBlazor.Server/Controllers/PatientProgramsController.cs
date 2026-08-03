using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Shared.Models;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientProgramsController : ControllerBase
{
    [HttpGet("patient/{patientId}")]
    public ActionResult<IEnumerable<PatientProgramDto>> GetByPatient(int patientId)
    {
        var programs = new List<PatientProgramDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                id,
                patient_id,
                COALESCE(titre, ''),
                COALESCE(nature_seances, ''),
                COALESCE(nb_seances, 0),
                COALESCE(duree_seance_minutes, 0),
                COALESCE(date_debut, ''),
                COALESCE(statut, ''),
                COALESCE(objectifs, ''),
                COALESCE(remarques, '')
            FROM patient_programs
            WHERE patient_id = @patientId
            ORDER BY date_debut, id
        ";
        cmd.Parameters.AddWithValue("@patientId", patientId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            programs.Add(new PatientProgramDto
            {
                Id = reader.GetInt32(0),
                PatientId = reader.GetInt32(1),
                Titre = reader.GetString(2),
                NatureSeances = reader.GetString(3),
                NbSeances = reader.GetInt32(4),
                DureeSeanceMinutes = reader.GetInt32(5),
                DateDebut = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                Statut = reader.GetString(7),
                Objectifs = reader.GetString(8),
                Remarques = reader.GetString(9),
            });
        }

        return Ok(programs);
    }

    [HttpPost]
    public ActionResult<PatientProgramDto> Create(PatientProgramDto program)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO patient_programs (
                patient_id, titre, nature_seances, nb_seances,
                duree_seance_minutes, date_debut, statut,
                objectifs, remarques
            ) VALUES (
                @patient_id, @titre, @nature_seances, @nb_seances,
                @duree_seance_minutes, @date_debut, @statut,
                @objectifs, @remarques
            )
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@patient_id", program.PatientId);
        cmd.Parameters.AddWithValue("@titre", (object?)program.Titre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nature_seances", (object?)program.NatureSeances ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nb_seances", program.NbSeances);
        cmd.Parameters.AddWithValue("@duree_seance_minutes", program.DureeSeanceMinutes);
        cmd.Parameters.AddWithValue("@date_debut", (object?)program.DateDebut ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@statut", (object?)program.Statut ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@objectifs", (object?)program.Objectifs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@remarques", (object?)program.Remarques ?? DBNull.Value);
        program.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return CreatedAtAction(nameof(GetByPatient), new { patientId = program.PatientId }, program);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, PatientProgramDto program)
    {
        if (id != program.Id)
        {
            return BadRequest("Le programme ID ne correspond pas.");
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE patient_programs
            SET
                patient_id = @patient_id,
                titre = @titre,
                nature_seances = @nature_seances,
                nb_seances = @nb_seances,
                duree_seance_minutes = @duree_seance_minutes,
                date_debut = @date_debut,
                statut = @statut,
                objectifs = @objectifs,
                remarques = @remarques
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@patient_id", program.PatientId);
        cmd.Parameters.AddWithValue("@titre", (object?)program.Titre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nature_seances", (object?)program.NatureSeances ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nb_seances", program.NbSeances);
        cmd.Parameters.AddWithValue("@duree_seance_minutes", program.DureeSeanceMinutes);
        cmd.Parameters.AddWithValue("@date_debut", (object?)program.DateDebut ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@statut", (object?)program.Statut ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@objectifs", (object?)program.Objectifs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@remarques", (object?)program.Remarques ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);

        var rowsUpdated = cmd.ExecuteNonQuery();
        if (rowsUpdated == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}
