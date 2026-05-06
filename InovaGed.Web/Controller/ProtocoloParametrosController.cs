using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Protocolo/Parametros")]
public sealed class ProtocoloParametrosController : GedControllerBase
{
    public ProtocoloParametrosController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (!PodeAdministrar()) return Forbid();
        using var db = await OpenAsync();

        var itens = await db.QueryAsync<ProtocoloParametroVM>(@"
select id as Id, chave as Chave, valor as Valor, descricao as Descricao, tipo as Tipo, grupo as Grupo, ativo as Ativo
from ged.protocolo_parametro
where tenant_id = @TenantId and reg_status = 'A'
order by grupo, chave;", new { TenantId });

        return View("~/Views/ProtocoloParametros/Index.cshtml", new ProtocoloParametrosIndexVM { Itens = itens.ToList() });
    }

    [HttpPost("Salvar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salvar(List<ProtocoloParametroVM> itens)
    {
        if (!PodeAdministrar()) return Forbid();
        using var db = await OpenAsync();

        foreach (var item in itens)
        {
            await db.ExecuteAsync(@"
update ged.protocolo_parametro
set valor = @Valor, descricao = @Descricao, ativo = @Ativo, updated_at = now(), updated_by = @UserId
where tenant_id = @TenantId and id = @Id;", new { TenantId, item.Id, item.Valor, item.Descricao, item.Ativo, UserId });
        }

        TempData["ok"] = "Parâmetros salvos com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    private bool PodeAdministrar() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Gestor) || User.IsInRole(AppRoles.Arquivista);
}
