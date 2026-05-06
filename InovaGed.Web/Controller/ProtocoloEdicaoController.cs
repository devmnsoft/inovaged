using System.Data;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Protocolo")]
public sealed class ProtocoloEdicaoController : GedControllerBase
{
    public ProtocoloEdicaoController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    [HttpGet("Editar/{id:guid}")]
    public async Task<IActionResult> Editar(Guid id)
    {
        using var db = await OpenAsync();
        var vm = await db.QuerySingleOrDefaultAsync<ProtocoloEditarVM>(@"
select id as Id, numero as Numero, status as Status, assunto as Assunto, especie as Especie,
tipo_solicitacao as TipoSolicitacao, procedencia as Procedencia, origem_pedido as OrigemPedido,
descricao as Descricao, informacoes_complementares as InformacoesComplementares, interessado as Interessado,
cpf_cnpj as CpfCnpj, email as Email, telefone as Telefone, solicitante_nome as SolicitanteNome,
solicitante_matricula as SolicitanteMatricula, solicitante_cargo as SolicitanteCargo, data_prazo as DataPrazo
from ged.protocolo
where tenant_id = @TenantId and id = @Id and reg_status = 'A';", new { TenantId, Id = id });

        if (vm == null) return NotFound();
        if (!await PodeEditarAsync(db, id))
        {
            TempData["erro"] = "Você não possui permissão para editar este protocolo.";
            return RedirectToAction("Details", "Protocolo", new { id });
        }

        return View("~/Views/ProtocoloEdicao/Editar.cshtml", vm);
    }

    [HttpPost("Editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(ProtocoloEditarVM vm)
    {
        using var db = await OpenAsync();

        if (!await PodeEditarAsync(db, vm.Id))
        {
            TempData["erro"] = "Você não possui permissão para editar este protocolo.";
            return RedirectToAction("Details", "Protocolo", new { id = vm.Id });
        }

        if (!ModelState.IsValid)
            return View("~/Views/ProtocoloEdicao/Editar.cshtml", vm);

        using var tx = db.BeginTransaction();
        try
        {
            await db.ExecuteAsync(@"
update ged.protocolo
set assunto=@Assunto, especie=@Especie, tipo_solicitacao=@TipoSolicitacao, procedencia=@Procedencia,
origem_pedido=@OrigemPedido, descricao=@Descricao, informacoes_complementares=@InformacoesComplementares,
interessado=@Interessado, cpf_cnpj=@CpfCnpj, email=@Email, telefone=@Telefone,
solicitante_nome=@SolicitanteNome, solicitante_matricula=@SolicitanteMatricula, solicitante_cargo=@SolicitanteCargo,
data_prazo=@DataPrazo, editado_por=@UserId, editado_por_nome=@UserName, data_edicao=now(),
justificativa_edicao=@JustificativaEdicao, updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@Id;", new
            {
                TenantId, vm.Id, vm.Assunto, vm.Especie, vm.TipoSolicitacao, vm.Procedencia, vm.OrigemPedido,
                vm.Descricao, vm.InformacoesComplementares, vm.Interessado, vm.CpfCnpj, vm.Email, vm.Telefone,
                vm.SolicitanteNome, vm.SolicitanteMatricula, vm.SolicitanteCargo, vm.DataPrazo, UserId,
                UserName = UserNameSafe, vm.JustificativaEdicao
            }, tx);

            await db.ExecuteAsync(@"
insert into ged.protocolo_auditoria
(tenant_id, protocolo_id, entidade, entidade_id, acao, valor_novo, usuario_id, usuario_nome, ip, user_agent)
values (@TenantId, @Id, 'protocolo', @Id, 'EDITAR', cast(@Json as jsonb), @UserId, @UserName, @Ip, @UserAgent);", new
            {
                TenantId, vm.Id,
                Json = System.Text.Json.JsonSerializer.Serialize(new { vm.Assunto, vm.Interessado, vm.JustificativaEdicao }),
                UserId, UserName = UserNameSafe,
                Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            }, tx);

            await db.ExecuteAsync(@"
insert into ged.protocolo_tramitacao
(id, tenant_id, protocolo_id, usuario_id, usuario_nome, acao, status_anterior, status_novo, justificativa, data_tramitacao, ip, user_agent, reg_status)
values (gen_random_uuid(), @TenantId, @Id, @UserId, @UserName, 'EDICAO', @Status, @Status, @Justificativa, now(), @Ip, @UserAgent, 'A');", new
            {
                TenantId, vm.Id, UserId, UserName = UserNameSafe, Status = vm.Status, Justificativa = vm.JustificativaEdicao,
                Ip = HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent = Request.Headers.UserAgent.ToString()
            }, tx);

            tx.Commit();
            TempData["ok"] = "Protocolo editado com sucesso.";
            return RedirectToAction("Details", "Protocolo", new { id = vm.Id });
        }
        catch { tx.Rollback(); throw; }
    }

    private async Task<bool> PodeEditarAsync(IDbConnection db, Guid protocoloId)
    {
        if (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Gestor)) return true;
        if (UserId == null) return false;

        return await db.ExecuteScalarAsync<bool>(@"
select exists (
    select 1 from ged.protocolo p
    join ged.protocolo_usuario_setor us on us.tenant_id=p.tenant_id and us.setor_id=p.setor_atual_id
    where p.tenant_id=@TenantId and p.id=@ProtocoloId and us.usuario_id=@UserId
      and us.ativo=true and us.reg_status='A'
      and p.status in ('RASCUNHO','ABERTO','EM_TRAMITACAO','RECEBIDO','DEVOLVIDO','AGUARDANDO_COMPLEMENTACAO')
);", new { TenantId, ProtocoloId = protocoloId, UserId });
    }
}
