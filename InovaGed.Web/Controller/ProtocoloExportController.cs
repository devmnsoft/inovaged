using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Protocolo/Exportar")]
public sealed class ProtocoloExportController : GedControllerBase
{
    public ProtocoloExportController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    [HttpGet("Csv")]
    public async Task<IActionResult> Csv(string? q, string? status, Guid? setorId, DateTime? de, DateTime? ate, bool somenteVencidos = false)
    {
        using var db = await OpenAsync();

        var rows = await db.QueryAsync<dynamic>(@"
select numero, assunto, interessado, status, setor_atual_nome, setor_origem_nome, created_at, data_prazo, vencido, total_anexos, total_movimentacoes
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  and (cast(@Q as text) is null or cast(@Q as text) = '' or numero ilike '%' || cast(@Q as text) || '%' or assunto ilike '%' || cast(@Q as text) || '%' or interessado ilike '%' || cast(@Q as text) || '%')
  and (cast(@Status as text) is null or cast(@Status as text) = '' or status = cast(@Status as text))
  and (cast(@SetorId as uuid) is null or setor_atual_id = cast(@SetorId as uuid))
  and (cast(@De as date) is null or created_at::date >= cast(@De as date))
  and (cast(@Ate as date) is null or created_at::date <= cast(@Ate as date))
  and (cast(@SomenteVencidos as boolean) = false or vencido = true)
order by created_at desc limit 10000;", new { TenantId, Q = q, Status = status, SetorId = setorId, De = de?.Date, Ate = ate?.Date, SomenteVencidos = somenteVencidos });

        var sb = new StringBuilder();
        sb.AppendLine("Numero;Assunto;Interessado;Status;Setor Atual;Setor Origem;Criado Em;Prazo;Vencido;Anexos;Movimentacoes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(";", Esc(r.numero), Esc(r.assunto), Esc(r.interessado), Esc(r.status), Esc(r.setor_atual_nome), Esc(r.setor_origem_nome), Esc(r.created_at), Esc(r.data_prazo), Esc(r.vencido), Esc(r.total_anexos), Esc(r.total_movimentacoes)));

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"protocolos_{DateTime.Now:yyyyMMddHHmmss}.csv");
    }

    private static string Esc(object? value) => $"\"{(Convert.ToString(value) ?? "").Replace("\"", "\"\"")}\"";
}
