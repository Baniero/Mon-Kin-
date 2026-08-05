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

    [HttpGet]
    public ActionResult<IEnumerable<CabinetDto>> GetAll()
    {
        if (!IsAdmin())
        {
            return Forbid();
        }

        var cabinets = new List<CabinetDto>();
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, nom_cabinet, code_etablissement, matricule_fiscal, nom_etablissement, racine, cle, qualite, numero_assuree
            FROM cabinets
            ORDER BY nom_cabinet
        ";

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
                Racine = reader.IsDBNull(5) ? null : reader.GetString(5),
                Cle = reader.IsDBNull(6) ? null : reader.GetString(6),
                Qualite = reader.IsDBNull(7) ? null : reader.GetString(7),
                NumeroAssuree = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return Ok(cabinets);
    }

    [HttpGet("{id}")]
    public ActionResult<CabinetDto> GetById(int id)
    {
        if (!IsAdmin())
        {
            return Forbid();
        }

        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, nom_cabinet, code_etablissement, matricule_fiscal, nom_etablissement, racine, cle, qualite, numero_assuree
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
            Racine = reader.IsDBNull(5) ? null : reader.GetString(5),
            Cle = reader.IsDBNull(6) ? null : reader.GetString(6),
            Qualite = reader.IsDBNull(7) ? null : reader.GetString(7),
            NumeroAssuree = reader.IsDBNull(8) ? null : reader.GetString(8)
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
            INSERT INTO cabinets (nom_cabinet, code_etablissement, matricule_fiscal, nom_etablissement, racine, cle, qualite, numero_assuree)
            VALUES (@nom_cabinet, @code_etablissement, @matricule_fiscal, @nom_etablissement, @racine, @cle, @qualite, @numero_assuree)
            RETURNING id
        ";
        cmd.Parameters.AddWithValue("@nom_cabinet", (object?)request.NomCabinet ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_etablissement", (object?)request.CodeEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@matricule_fiscal", (object?)request.MatriculeFiscal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_etablissement", (object?)request.NomEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@racine", (object?)request.Racine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cle", (object?)request.Cle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qualite", (object?)request.Qualite ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_assuree", (object?)request.NumeroAssuree ?? DBNull.Value);

        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return CreatedAtAction(nameof(GetById), new { id }, new CabinetDto
        {
            Id = id,
            NomCabinet = request.NomCabinet,
            CodeEtablissement = request.CodeEtablissement,
            MatriculeFiscal = request.MatriculeFiscal,
            NomEtablissement = request.NomEtablissement,
            Racine = request.Racine,
            Cle = request.Cle,
            Qualite = request.Qualite,
            NumeroAssuree = request.NumeroAssuree
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
                racine = @racine,
                cle = @cle,
                qualite = @qualite,
                numero_assuree = @numero_assuree
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@nom_cabinet", (object?)request.NomCabinet ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_etablissement", (object?)request.CodeEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@matricule_fiscal", (object?)request.MatriculeFiscal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_etablissement", (object?)request.NomEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@racine", (object?)request.Racine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cle", (object?)request.Cle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qualite", (object?)request.Qualite ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_assuree", (object?)request.NumeroAssuree ?? DBNull.Value);
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
