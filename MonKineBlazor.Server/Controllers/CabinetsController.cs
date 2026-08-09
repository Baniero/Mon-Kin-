using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Server.Services;
using MonKineBlazor.Shared.Models;
using Npgsql;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CabinetsController : ControllerBase
{
    private bool IsAdmin() => UserContextHelper.IsAdmin(HttpContext);
    private UserDto? GetCurrentUser() => UserContextHelper.GetCurrentUser(HttpContext);

    [HttpGet]
    public ActionResult<IEnumerable<CabinetDto>> GetAll()
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var cabinets = new List<CabinetDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();

        if (IsAdmin())
        {
            cmd.CommandText = @"
                SELECT id, nom_cabinet, code_etablissement, matricule_fiscal, nom_etablissement, racine, cle, qualite,
                       adresse_cabinet, nom_cabinet_arabe, nom_kine_arabe, adresse_kine_arabe, telephone, rib,
                       programme_type_options, nature_seances_options
                FROM cabinets
                ORDER BY nom_cabinet
            ";
        }
        else if (currentUser.CabinetId.HasValue)
        {
            cmd.CommandText = @"
                SELECT id, nom_cabinet, code_etablissement, matricule_fiscal, nom_etablissement, racine, cle, qualite,
                       adresse_cabinet, nom_cabinet_arabe, nom_kine_arabe, adresse_kine_arabe, telephone, rib,
                       programme_type_options, nature_seances_options
                FROM cabinets
                WHERE id = @cabinet_id
            ";
            cmd.Parameters.AddWithValue("@cabinet_id", currentUser.CabinetId.Value);
        }
        else
        {
            return Forbid();
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            cabinets.Add(new CabinetDto
            {
                Id = reader.GetInt32(0),
                NomCabinet = reader.IsDBNull(1) ? null : reader.GetString(1),
                CodeEtablissement = reader.IsDBNull(2) ? null : reader.GetString(2),
                MatriculeFiscal = reader.IsDBNull(3) ? null : reader.GetString(3),
                NomEtablissement = reader.IsDBNull(4) ? null : reader.GetString(4),
                NumeroEmployeur = reader.IsDBNull(5) ? null : reader.GetString(5),
                CodeCnam = reader.IsDBNull(6) ? null : reader.GetString(6),
                Qualite = reader.IsDBNull(7) ? null : reader.GetString(7),
                AdresseCabinet = reader.IsDBNull(8) ? null : reader.GetString(8),
                NomCabinetArabe = reader.IsDBNull(9) ? null : reader.GetString(9),
                NomKineArabe = reader.IsDBNull(10) ? null : reader.GetString(10),
                AdresseKineArabe = reader.IsDBNull(11) ? null : reader.GetString(11),
                Telephone = reader.IsDBNull(12) ? null : reader.GetString(12),
                Rib = reader.IsDBNull(13) ? null : reader.GetString(13),
                ProgrammeTypeOptions = reader.IsDBNull(14) ? null : reader.GetString(14),
                NatureSeancesOptions = reader.IsDBNull(15) ? null : reader.GetString(15)
            });
        }

        return Ok(cabinets);
    }

    [HttpGet("{id}")]
    public ActionResult<CabinetDto> GetById(int id)
    {
        if (!IsAdmin() && GetCurrentUser()?.CabinetId != id)
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, nom_cabinet, code_etablissement, matricule_fiscal, nom_etablissement, racine, cle, qualite,
                   adresse_cabinet, nom_cabinet_arabe, nom_kine_arabe, adresse_kine_arabe, telephone, rib,
                   programme_type_options, nature_seances_options
            FROM cabinets
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return NotFound();
        }

        return Ok(new CabinetDto
        {
            Id = reader.GetInt32(0),
            NomCabinet = reader.IsDBNull(1) ? null : reader.GetString(1),
            CodeEtablissement = reader.IsDBNull(2) ? null : reader.GetString(2),
            MatriculeFiscal = reader.IsDBNull(3) ? null : reader.GetString(3),
            NomEtablissement = reader.IsDBNull(4) ? null : reader.GetString(4),
            NumeroEmployeur = reader.IsDBNull(5) ? null : reader.GetString(5),
            CodeCnam = reader.IsDBNull(6) ? null : reader.GetString(6),
            Qualite = reader.IsDBNull(7) ? null : reader.GetString(7),
            AdresseCabinet = reader.IsDBNull(8) ? null : reader.GetString(8),
            NomCabinetArabe = reader.IsDBNull(9) ? null : reader.GetString(9),
            NomKineArabe = reader.IsDBNull(10) ? null : reader.GetString(10),
            AdresseKineArabe = reader.IsDBNull(11) ? null : reader.GetString(11),
            Telephone = reader.IsDBNull(12) ? null : reader.GetString(12),
            Rib = reader.IsDBNull(13) ? null : reader.GetString(13),
            ProgrammeTypeOptions = reader.IsDBNull(14) ? null : reader.GetString(14),
            NatureSeancesOptions = reader.IsDBNull(15) ? null : reader.GetString(15)
        });
    }

    [HttpPost]
    public ActionResult<CabinetDto> Create(CabinetCreateRequestDto request)
    {
        if (!IsAdmin())
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cabinets (nom_cabinet, code_etablissement, matricule_fiscal, nom_etablissement, racine, cle, qualite,
                                  adresse_cabinet, nom_cabinet_arabe, nom_kine_arabe, adresse_kine_arabe, telephone, rib,
                                  programme_type_options, nature_seances_options)
            VALUES (@nom_cabinet, @code_etablissement, @matricule_fiscal, @nom_etablissement, @numero_employeur, @code_cnam, @qualite,
                    @adresse_cabinet, @nom_cabinet_arabe, @nom_kine_arabe, @adresse_kine_arabe, @telephone, @rib,
                    @programme_type_options, @nature_seances_options)
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@nom_cabinet", (object?)request.NomCabinet ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_etablissement", (object?)request.CodeEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@matricule_fiscal", (object?)request.MatriculeFiscal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_etablissement", (object?)request.NomEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_employeur", (object?)request.NumeroEmployeur ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_cnam", (object?)request.CodeCnam ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qualite", (object?)request.Qualite ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@adresse_cabinet", (object?)request.AdresseCabinet ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_cabinet_arabe", (object?)request.NomCabinetArabe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_kine_arabe", (object?)request.NomKineArabe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@adresse_kine_arabe", (object?)request.AdresseKineArabe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telephone", (object?)request.Telephone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rib", (object?)request.Rib ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@programme_type_options", (object?)request.ProgrammeTypeOptions ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nature_seances_options", (object?)request.NatureSeancesOptions ?? DBNull.Value);

        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return CreatedAtAction(nameof(GetById), new { id }, new CabinetDto
        {
            Id = id,
            NomCabinet = request.NomCabinet,
            CodeEtablissement = request.CodeEtablissement,
            MatriculeFiscal = request.MatriculeFiscal,
            NomEtablissement = request.NomEtablissement,
            NumeroEmployeur = request.NumeroEmployeur,
            CodeCnam = request.CodeCnam,
            Qualite = request.Qualite,
            AdresseCabinet = request.AdresseCabinet,
            NomCabinetArabe = request.NomCabinetArabe,
            NomKineArabe = request.NomKineArabe,
            AdresseKineArabe = request.AdresseKineArabe,
            Telephone = request.Telephone,
            Rib = request.Rib,
            ProgrammeTypeOptions = request.ProgrammeTypeOptions,
            NatureSeancesOptions = request.NatureSeancesOptions
        });
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, CabinetUpdateRequestDto request)
    {
        if (!IsAdmin())
        {
            return Forbid();
        }

        if (id != request.Id)
        {
            return BadRequest("Le cabinet ID ne correspond pas.");
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE cabinets
            SET nom_cabinet = @nom_cabinet,
                code_etablissement = @code_etablissement,
                matricule_fiscal = @matricule_fiscal,
                nom_etablissement = @nom_etablissement,
                racine = @numero_employeur,
                cle = @code_cnam,
                qualite = @qualite,
                adresse_cabinet = @adresse_cabinet,
                nom_cabinet_arabe = @nom_cabinet_arabe,
                nom_kine_arabe = @nom_kine_arabe,
                adresse_kine_arabe = @adresse_kine_arabe,
                telephone = @telephone,
                rib = @rib,
                programme_type_options = @programme_type_options,
                nature_seances_options = @nature_seances_options
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@nom_cabinet", (object?)request.NomCabinet ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_etablissement", (object?)request.CodeEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@matricule_fiscal", (object?)request.MatriculeFiscal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_etablissement", (object?)request.NomEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_employeur", (object?)request.NumeroEmployeur ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_cnam", (object?)request.CodeCnam ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qualite", (object?)request.Qualite ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@adresse_cabinet", (object?)request.AdresseCabinet ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_cabinet_arabe", (object?)request.NomCabinetArabe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_kine_arabe", (object?)request.NomKineArabe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@adresse_kine_arabe", (object?)request.AdresseKineArabe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telephone", (object?)request.Telephone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rib", (object?)request.Rib ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@programme_type_options", (object?)request.ProgrammeTypeOptions ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nature_seances_options", (object?)request.NatureSeancesOptions ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        if (!IsAdmin())
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cabinets WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var rows = cmd.ExecuteNonQuery();
        return rows == 0 ? NotFound() : NoContent();
    }
}
