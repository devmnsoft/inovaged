using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Protocolo/Ged")]
public sealed class ProtocoloGedIntegracaoController : GedControllerBase
{
    public ProtocoloGedIntegracaoController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    [HttpGet("Vinculos/{protocoloId:guid}")]
    public async Task<IActionResult> Vinculos(Guid protocoloId)
    {
        using var db = await OpenAsync();

        var numero = await db.ExecuteScalarAsync<string?>(
            "select numero from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A';",
            new { TenantId, Id = protocoloId });

        if (numero == null) return NotFound();

        var vm = new ProtocoloGedVincularVM
        {
            ProtocoloId = protocoloId,
            ProtocoloNumero = numero,
            Anexos = await GetAnexosAsync(db, protocoloId),
            Vinculos = (await db.QueryAsync<ProtocoloGedVinculoVM>(@"
select id as Id, protocolo_id as ProtocoloId, protocolo_numero as ProtocoloNumero,
protocolo_documento_id as ProtocoloDocumentoId, protocolo_anexo_nome as ProtocoloAnexoNome,
ged_document_id as GedDocumentId, tipo_vinculo as TipoVinculo, observacao as Observacao,
criado_por_nome as CriadoPorNome, created_at as CreatedAt
from ged.vw_protocolo_ged_vinculos
where tenant_id=@TenantId and protocolo_id=@ProtocoloId
order by created_at desc;", new { TenantId, ProtocoloId = protocoloId })).ToList()
        };

        return View("~/Views/ProtocoloGed/Vinculos.cshtml", vm);
    }

    [HttpPost("Vincular")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Vincular(ProtocoloGedVincularVM vm)
    {
        using var db = await OpenAsync();

        await db.ExecuteAsync(@"
insert into ged.protocolo_documento_ged
(id, tenant_id, protocolo_id, protocolo_documento_id, ged_document_id, tipo_vinculo, observacao, criado_por, criado_por_nome, created_at, reg_status)
values (gen_random_uuid(), @TenantId, @ProtocoloId, @ProtocoloDocumentoId, @GedDocumentId, @TipoVinculo, @Observacao, @UserId, @UserName, now(), 'A');", new
        {
            TenantId, vm.ProtocoloId, vm.ProtocoloDocumentoId, vm.GedDocumentId, vm.TipoVinculo, vm.Observacao, UserId, UserName = UserNameSafe
        });

        TempData["ok"] = "Documento GED vinculado ao protocolo.";
        return RedirectToAction(nameof(Vinculos), new { protocoloId = vm.ProtocoloId });
    }

    [HttpPost("RemoverVinculo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoverVinculo(Guid id, Guid protocoloId)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Gestor) && !User.IsInRole(AppRoles.Arquivista))
            return Forbid();

        using var db = await OpenAsync();
        await db.ExecuteAsync("update ged.protocolo_documento_ged set reg_status='E' where tenant_id=@TenantId and id=@Id;", new { TenantId, Id = id });

        TempData["ok"] = "Vínculo removido.";
        return RedirectToAction(nameof(Vinculos), new { protocoloId });
    }

    private async Task<List<SelectListItem>> GetAnexosAsync(System.Data.IDbConnection db, Guid protocoloId)
    {
        var rows = await db.QueryAsync<(Guid Id, string NomeArquivo)>(
            "select id, nome_arquivo from ged.protocolo_documento where tenant_id=@TenantId and protocolo_id=@ProtocoloId and reg_status='A' order by created_at desc;",
            new { TenantId, ProtocoloId = protocoloId });

        return rows.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.NomeArquivo }).ToList();
    }
}
