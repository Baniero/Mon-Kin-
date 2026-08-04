using Microsoft.AspNetCore.Mvc;
using MonKineBlazor.Server.Data;
using MonKineBlazor.Shared.Models;
using Npgsql;

namespace MonKineBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CabinetInfoController : ControllerBase
{
    [HttpGet]
    public ActionResult<CabinetInfoDto> Get()
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, nom_cabinet, racine, cle, qualite, numero_assuree, code_etablissement, matricule_fiscal, nom_etablissement
            FROM cabinet_info
            ORDER BY id DESC
            LIMIT 1
        ";

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return Ok(new CabinetInfoDto());
        }

        return Ok(new CabinetInfoDto
        {
            Id = reader.GetInt32(0),
            NomCabinet = reader.IsDBNull(1) ? null : reader.GetString(1),
            Racine = reader.IsDBNull(2) ? null : reader.GetString(2),
            Cle = reader.IsDBNull(3) ? null : reader.GetString(3),
            Qualite = reader.IsDBNull(4) ? null : reader.GetString(4),
            NumeroAssuree = reader.IsDBNull(5) ? null : reader.GetString(5),
            CodeEtablissement = reader.IsDBNull(6) ? null : reader.GetString(6),
            MatriculeFiscal = reader.IsDBNull(7) ? null : reader.GetString(7),
            NomEtablissement = reader.IsDBNull(8) ? null : reader.GetString(8)
        });
    }

    [HttpPut]
    public IActionResult Update(CabinetInfoRequestDto request)
    {
        using var conn = DatabaseConnectionProvider.CreateConnection();
        conn.Open();

        using var tran = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cabinet_info (nom_cabinet, racine, cle, qualite, numero_assuree, code_etablissement, matricule_fiscal, nom_etablissement)
            VALUES (@nom_cabinet, @racine, @cle, @qualite, @numero_assuree, @code_etablissement, @matricule_fiscal, @nom_etablissement)
        ";
        cmd.Transaction = tran;
        cmd.Parameters.AddWithValue("@nom_cabinet", (object?)request.NomCabinet ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@racine", (object?)request.Racine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cle", (object?)request.Cle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qualite", (object?)request.Qualite ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numero_assuree", (object?)request.NumeroAssuree ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code_etablissement", (object?)request.CodeEtablissement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@matricule_fiscal", (object?)request.MatriculeFiscal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nom_etablissement", (object?)request.NomEtablissement ?? DBNull.Value);

        cmd.ExecuteNonQuery();
        tran.Commit();

        return NoContent();
    }
}
