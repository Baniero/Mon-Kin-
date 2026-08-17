using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Server.Services;
using MonKineBlazor.Shared.Models;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientProgramsController : ControllerBase
{
    private UserDto? GetCurrentUser() => UserContextHelper.GetCurrentUser(HttpContext);
    private bool IsAdmin() => UserContextHelper.IsAdmin(HttpContext);
    private bool IsPatientAccessible(int patientId) => UserContextHelper.IsPatientAccessible(HttpContext, patientId);
    private bool IsProgramAccessible(int programId)
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
            FROM patient_programs pp
            JOIN patients p ON p.id = pp.patient_id
            WHERE pp.id = @programId
              AND p.cabinet_id = @cabinet_id
        ";
        cmd.Parameters.AddWithValue("@programId", programId);
        cmd.Parameters.AddWithValue("@cabinet_id", currentUser.CabinetId.Value);

        return cmd.ExecuteScalar() != null;
    }

    [HttpGet("patient/{patientId}")]
    public ActionResult<IEnumerable<PatientProgramDto>> GetByPatient(int patientId)
    {
        if (!IsAdmin() && !IsPatientAccessible(patientId))
        {
            return Forbid();
        }

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
                COALESCE(nb_seances_par_semaine, 1),
                COALESCE(duree_seance_minutes, 0),
                date_debut,
                date_fin,
                COALESCE(code_bureau, ''),
                COALESCE(annee, ''),
                COALESCE(numero_decision, ''),
                COALESCE(numero_ordre, ''),
                COALESCE(type_programme, ''),
                COALESCE(prix_unitaire, 0),
                COALESCE(prix_espece, 0),
                COALESCE(prix_ht, 0),
                COALESCE(tva, 0),
                COALESCE(montant_tva, 0),
                COALESCE(prix_ttc, 0),
                COALESCE(prix_seance_ttc, 0),
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
                NbSeancesParSemaine = reader.GetInt32(5),
                DureeSeanceMinutes = reader.GetInt32(6),
                DateDebut = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                DateFin = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                CodeBureau = reader.GetString(9),
                Annee = reader.GetString(10),
                NumeroDecision = reader.GetString(11),
                NumeroOrdre = reader.GetString(12),
                TypeProgramme = reader.GetString(13),
                PrixUnitaire = reader.GetDecimal(14),
                PrixEspece = reader.GetDecimal(15),
                PrixHT = reader.GetDecimal(16),
                TVA = reader.GetDecimal(17),
                MontantTVA = reader.GetDecimal(18),
                PrixTTC = reader.GetDecimal(19),
                PrixSeanceTTC = reader.GetDecimal(20),
                Statut = reader.GetString(21),
                Objectifs = reader.GetString(22),
                Remarques = reader.GetString(23),
            });
        }

        return Ok(programs);
    }

    [HttpPost]
    public ActionResult<PatientProgramDto> Create(PatientProgramDto program)
    {
        if (!IsAdmin() && !IsPatientAccessible(program.PatientId))
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO patient_programs (
                patient_id, titre, nature_seances, nb_seances, nb_seances_par_semaine,
                duree_seance_minutes, date_debut, date_fin, code_bureau, annee,
                numero_decision, numero_ordre, type_programme, prix_unitaire, prix_espece, prix_ht, tva,
                montant_tva, prix_ttc, prix_seance_ttc, statut, objectifs, remarques
            ) VALUES (
                @patient_id, @titre, @nature_seances, @nb_seances, @nb_seances_par_semaine,
                @duree_seance_minutes, @date_debut, @date_fin, @code_bureau, @annee,
                @numero_decision, @numero_ordre, @type_programme, @prix_unitaire, @prix_espece, @prix_ht, @tva,
                @montant_tva, @prix_ttc, @prix_seance_ttc, @statut, @objectifs, @remarques
            )
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@patient_id", program.PatientId);
        cmd.Parameters.AddWithValue("@titre", (object?)program.Titre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nature_seances", (object?)program.NatureSeances ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nb_seances", program.NbSeances);
        cmd.Parameters.AddWithValue("@nb_seances_par_semaine", program.NbSeancesParSemaine);
        cmd.Parameters.AddWithValue("@duree_seance_minutes", program.DureeSeanceMinutes);
        cmd.Parameters.AddWithValue("@date_debut", (object?)program.DateDebut ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date_fin", (object?)program.DateFin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_bureau", (object?)program.CodeBureau ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@annee", (object?)program.Annee ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_decision", (object?)program.NumeroDecision ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_ordre", (object?)program.NumeroOrdre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type_programme", (object?)program.TypeProgramme ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prix_unitaire", program.PrixUnitaire);
        cmd.Parameters.AddWithValue("@prix_espece", program.PrixEspece);
        cmd.Parameters.AddWithValue("@prix_ht", program.PrixHT);
        cmd.Parameters.AddWithValue("@tva", program.TVA);
        cmd.Parameters.AddWithValue("@montant_tva", program.MontantTVA);
        cmd.Parameters.AddWithValue("@prix_ttc", program.PrixTTC);
        cmd.Parameters.AddWithValue("@prix_seance_ttc", program.PrixSeanceTTC);
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

        if (!IsAdmin() && !IsProgramAccessible(id))
        {
            return Forbid();
        }

        if (!IsAdmin() && !IsPatientAccessible(program.PatientId))
        {
            return Forbid();
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
                nb_seances_par_semaine = @nb_seances_par_semaine,
                duree_seance_minutes = @duree_seance_minutes,
                date_debut = @date_debut,
                date_fin = @date_fin,
                code_bureau = @code_bureau,
                annee = @annee,
                numero_decision = @numero_decision,
                numero_ordre = @numero_ordre,
                type_programme = @type_programme,
                prix_unitaire = @prix_unitaire,
                prix_espece = @prix_espece,
                prix_ht = @prix_ht,
                tva = @tva,
                montant_tva = @montant_tva,
                prix_ttc = @prix_ttc,
                prix_seance_ttc = @prix_seance_ttc,
                statut = @statut,
                objectifs = @objectifs,
                remarques = @remarques
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@patient_id", program.PatientId);
        cmd.Parameters.AddWithValue("@titre", (object?)program.Titre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nature_seances", (object?)program.NatureSeances ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nb_seances", program.NbSeances);
        cmd.Parameters.AddWithValue("@nb_seances_par_semaine", program.NbSeancesParSemaine);
        cmd.Parameters.AddWithValue("@duree_seance_minutes", program.DureeSeanceMinutes);
        cmd.Parameters.AddWithValue("@date_debut", (object?)program.DateDebut ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date_fin", (object?)program.DateFin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_bureau", (object?)program.CodeBureau ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@annee", (object?)program.Annee ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_decision", (object?)program.NumeroDecision ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_ordre", (object?)program.NumeroOrdre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type_programme", (object?)program.TypeProgramme ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prix_unitaire", program.PrixUnitaire);
        cmd.Parameters.AddWithValue("@prix_espece", program.PrixEspece);
        cmd.Parameters.AddWithValue("@prix_ht", program.PrixHT);
        cmd.Parameters.AddWithValue("@tva", program.TVA);
        cmd.Parameters.AddWithValue("@montant_tva", program.MontantTVA);
        cmd.Parameters.AddWithValue("@prix_ttc", program.PrixTTC);
        cmd.Parameters.AddWithValue("@prix_seance_ttc", program.PrixSeanceTTC);
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

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        if (!IsAdmin() && !IsProgramAccessible(id))
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM patient_programs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var rowsDeleted = cmd.ExecuteNonQuery();
        if (rowsDeleted == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}
