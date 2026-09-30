using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.ProtocolManage)]
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
        using var tx = db.BeginTransaction();
        var protocoloOk = await db.ExecuteScalarAsync<bool>(
            "select exists(select 1 from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A')",
            new { TenantId, Id = vm.ProtocoloId }, tx);
        if (!protocoloOk) return NotFound();

        if (vm.ProtocoloDocumentoId is Guid anexoId && anexoId != Guid.Empty)
        {
            var anexoOk = await db.ExecuteScalarAsync<bool>(
                "select exists(select 1 from ged.protocolo_documento where tenant_id=@TenantId and id=@Id and protocolo_id=@ProtocoloId and reg_status='A')",
                new { TenantId, Id = anexoId, vm.ProtocoloId }, tx);
            if (!anexoOk)
            {
                TempData["erro"] = "O anexo não pertence a este protocolo.";
                return RedirectToAction(nameof(Vinculos), new { protocoloId = vm.ProtocoloId });
            }
        }

        var documentoOk = await db.ExecuteScalarAsync<bool>(
            "select exists(select 1 from ged.document where tenant_id=@TenantId and id=@Id and reg_status='A')",
            new { TenantId, Id = vm.GedDocumentId }, tx);
        if (!documentoOk)
        {
            TempData["erro"] = "Documento GED inexistente neste tenant.";
            return RedirectToAction(nameof(Vinculos), new { protocoloId = vm.ProtocoloId });
        }

        var admin = RolePolicyHelper.IsFullAdmin(User) || User.IsInRole(AppRoles.Gestor) || User.IsInNormalizedRole(AppRoles.AdministradorOphir);
        if (await db.ExecuteScalarAsync<bool>("select to_regclass('ged.document_acl') is not null", transaction: tx))
        {
            var restrito = await db.ExecuteScalarAsync<bool>(
                "select exists(select 1 from ged.document_acl where document_id=@Id)",
                new { Id = vm.GedDocumentId }, tx);
            if (restrito && !admin)
            {
                var permitido = UserId.HasValue && await db.ExecuteScalarAsync<bool>(
                    "select exists(select 1 from ged.document_acl where document_id=@Id and user_id=@UserId and can_read=true)",
                    new { Id = vm.GedDocumentId, UserId }, tx);
                if (!permitido) return Forbid();
            }
        }

        var vinculoId = Guid.NewGuid();
        await db.ExecuteAsync(@"
insert into ged.protocolo_documento_ged
(id, tenant_id, protocolo_id, protocolo_documento_id, ged_document_id, tipo_vinculo, observacao, criado_por, criado_por_nome, created_at, reg_status)
values (@Id, @TenantId, @ProtocoloId, @ProtocoloDocumentoId, @GedDocumentId, @TipoVinculo, @Observacao, @UserId, @UserName, now(), 'A');", new
        {
            Id = vinculoId, TenantId, vm.ProtocoloId, vm.ProtocoloDocumentoId, vm.GedDocumentId, vm.TipoVinculo, vm.Observacao, UserId, UserName = UserNameSafe
        }, tx);
        await db.ExecuteAsync(@"
insert into ged.protocolo_auditoria
(tenant_id, protocolo_id, entidade, entidade_id, acao, valor_novo, usuario_id, usuario_nome, ip, user_agent)
values (@TenantId, @ProtocoloId, 'protocolo_documento_ged', @Id, 'GED_VINCULO', cast(@Json as jsonb), @UserId, @UserName, @Ip, @Ua);", new
        {
            TenantId, vm.ProtocoloId, Id = vinculoId,
            Json = System.Text.Json.JsonSerializer.Serialize(new { vm.GedDocumentId, vm.ProtocoloDocumentoId, vm.TipoVinculo }),
            UserId, UserName = UserNameSafe,
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Ua = Request.Headers.UserAgent.ToString()
        }, tx);
        tx.Commit();
        TempData["ok"] = "Documento GED vinculado ao protocolo.";
        return RedirectToAction(nameof(Vinculos), new { protocoloId = vm.ProtocoloId });
    }

    [HttpPost("RemoverVinculo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoverVinculo(Guid id, Guid protocoloId)
    {
        if (!RolePolicyHelper.IsFullAdmin(User) && !User.IsInRole(AppRoles.Gestor) && !User.IsInRole(AppRoles.Arquivista) && !User.IsInNormalizedRole(AppRoles.ArquivistaOphir))
            return Forbid();

        using var db = await OpenAsync();
        using var tx = db.BeginTransaction();
        var removed = await db.ExecuteAsync(
            "update ged.protocolo_documento_ged set reg_status='E' where tenant_id=@TenantId and id=@Id and protocolo_id=@ProtocoloId and reg_status='A';",
            new { TenantId, Id = id, ProtocoloId = protocoloId }, tx);
        if (removed == 0)
        {
            TempData["erro"] = "Vínculo não encontrado neste protocolo.";
            return RedirectToAction(nameof(Vinculos), new { protocoloId });
        }
        await db.ExecuteAsync(@"
insert into ged.protocolo_auditoria
(tenant_id, protocolo_id, entidade, entidade_id, acao, valor_novo, usuario_id, usuario_nome, ip, user_agent)
values (@TenantId, @ProtocoloId, 'protocolo_documento_ged', @Id, 'GED_VINCULO_REMOVIDO', cast(@Json as jsonb), @UserId, @UserName, @Ip, @Ua);", new
        {
            TenantId, ProtocoloId = protocoloId, Id = id,
            Json = System.Text.Json.JsonSerializer.Serialize(new { id, protocoloId }),
            UserId, UserName = UserNameSafe,
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Ua = Request.Headers.UserAgent.ToString()
        }, tx);
        tx.Commit();
        TempData["ok"] = "Vínculo removido. O documento GED não foi excluído.";
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
