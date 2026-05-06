using System.Data;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Protocolo")]
public sealed class ProtocoloMelhoriasController : GedControllerBase
{
    public ProtocoloMelhoriasController(IDbConnectionFactory dbFactory) : base(dbFactory)
    {
    }

    [HttpGet("Dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        using var db = await OpenAsync();

        var setores = await GetSetoresUsuarioIdsAsync(db);
        var isAdmin = IsAdminOrGestor();

        var filtroSetor = isAdmin ? "" : " and setor_atual_id = any(@Setores) ";

        var vm = new ProtocoloDashboardVM();

        vm.TotalEntrada = await db.ExecuteScalarAsync<int>($@"
select count(*)
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  and status not in ('FINALIZADO','ARQUIVADO','CANCELADO','DEFERIDO','INDEFERIDO')
  {filtroSetor};", new { TenantId, Setores = setores });

        vm.TotalAbertos = await db.ExecuteScalarAsync<int>($@"
select count(*)
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  and status = 'ABERTO'
  {filtroSetor};", new { TenantId, Setores = setores });

        vm.TotalEmTramitacao = await db.ExecuteScalarAsync<int>($@"
select count(*)
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  and status = 'EM_TRAMITACAO'
  {filtroSetor};", new { TenantId, Setores = setores });

        vm.TotalVencidos = await db.ExecuteScalarAsync<int>($@"
select count(*)
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  and vencido = true
  {filtroSetor};", new { TenantId, Setores = setores });

        vm.TotalFinalizadosMes = await db.ExecuteScalarAsync<int>($@"
select count(*)
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  and status in ('FINALIZADO','DEFERIDO','INDEFERIDO')
  and coalesce(data_finalizacao, created_at) >= date_trunc('month', now())
  {filtroSetor};", new { TenantId, Setores = setores });

        vm.TotalArquivadosMes = await db.ExecuteScalarAsync<int>($@"
select count(*)
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  and status = 'ARQUIVADO'
  and coalesce(data_arquivamento, created_at) >= date_trunc('month', now())
  {filtroSetor};", new { TenantId, Setores = setores });

        vm.TempoMedioHoras = await db.ExecuteScalarAsync<decimal>($@"
select coalesce(avg(extract(epoch from (coalesce(data_finalizacao, data_arquivamento, now()) - created_at)) / 3600), 0)
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  {filtroSetor};", new { TenantId, Setores = setores });

        vm.PorStatus = (await db.QueryAsync<ProtocoloDashboardStatusVM>($@"
select status as Status, count(*)::int as Total
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  {filtroSetor}
group by status
order by count(*) desc;", new { TenantId, Setores = setores })).ToList();

        vm.PorSetor = (await db.QueryAsync<ProtocoloDashboardSetorVM>($@"
select coalesce(setor_atual_nome, 'Sem setor') as Setor, count(*)::int as Total
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  {filtroSetor}
group by coalesce(setor_atual_nome, 'Sem setor')
order by count(*) desc
limit 10;", new { TenantId, Setores = setores })).ToList();

        vm.Ultimos = (await db.QueryAsync<ProtocoloRelatorioRowVM>($@"
select
    id as Id,
    numero as Numero,
    assunto as Assunto,
    interessado as Interessado,
    status as Status,
    setor_atual_nome as SetorAtualNome,
    setor_origem_nome as SetorOrigemNome,
    created_at as CreatedAt,
    data_prazo as DataPrazo,
    vencido as Vencido,
    dias_para_vencer as DiasParaVencer,
    total_anexos as TotalAnexos,
    total_movimentacoes as TotalMovimentacoes
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  {filtroSetor}
order by created_at desc
limit 10;", new { TenantId, Setores = setores })).ToList();

        return View("~/Views/ProtocoloMelhorias/Dashboard.cshtml", vm);
    }

    [HttpGet("Comprovante/{id:guid}")]
    public async Task<IActionResult> Comprovante(Guid id)
    {
        var vm = await MontarComprovanteAsync(id);
        if (vm == null) return NotFound();
        return View("~/Views/ProtocoloMelhorias/Comprovante.cshtml", vm);
    }

    [HttpGet("Capa/{id:guid}")]
    public async Task<IActionResult> Capa(Guid id)
    {
        var vm = await MontarComprovanteAsync(id);
        if (vm == null) return NotFound();
        return View("~/Views/ProtocoloMelhorias/Capa.cshtml", vm);
    }

    [HttpGet("Relatorios")]
    public async Task<IActionResult> Relatorios([FromQuery] ProtocoloRelatorioFiltroVM filtro)
    {
        using var db = await OpenAsync();

        filtro.Setores = await GetSetoresSelectAsync(db);

        var sql = @"
select
    id as ""Id"",
    numero as ""Numero"",
    assunto as ""Assunto"",
    interessado as ""Interessado"",
    status as ""Status"",
    setor_atual_nome as ""SetorAtualNome"",
    setor_origem_nome as ""SetorOrigemNome"",
    created_at as ""CreatedAt"",
    data_prazo as ""DataPrazo"",
    vencido as ""Vencido"",
    dias_para_vencer as ""DiasParaVencer"",
    total_anexos as ""TotalAnexos"",
    total_movimentacoes as ""TotalMovimentacoes""
from ged.vw_protocolo_resumo
where tenant_id = @TenantId
  and (cast(@Q as text) is null or cast(@Q as text) = '' or numero ilike '%' || cast(@Q as text) || '%' or assunto ilike '%' || cast(@Q as text) || '%' or interessado ilike '%' || cast(@Q as text) || '%')
  and (cast(@Status as text) is null or cast(@Status as text) = '' or status = cast(@Status as text))
  and (cast(@SetorId as uuid) is null or setor_atual_id = cast(@SetorId as uuid))
  and (cast(@De as date) is null or created_at::date >= cast(@De as date))
  and (cast(@Ate as date) is null or created_at::date <= cast(@Ate as date))
  and (cast(@SomenteVencidos as boolean) = false or vencido = true)
order by created_at desc
limit 1000;";

        filtro.Itens = (await db.QueryAsync<ProtocoloRelatorioRowVM>(sql, new
        {
            TenantId,
            Q = filtro.Q,
            Status = filtro.Status,
            SetorId = filtro.SetorId,
            De = filtro.De?.Date,
            Ate = filtro.Ate?.Date,
            SomenteVencidos = filtro.SomenteVencidos
        })).ToList();

        return View("~/Views/ProtocoloMelhorias/Relatorios.cshtml", filtro);
    }

    [HttpGet("Notificacoes")]
    public async Task<IActionResult> Notificacoes()
    {
        using var db = await OpenAsync();

        var setores = await GetSetoresUsuarioIdsAsync(db);

        var rows = await db.QueryAsync<ProtocoloNotificacaoVM>(@"
select
    n.id as Id,
    n.protocolo_id as ProtocoloId,
    p.numero as Numero,
    n.titulo as Titulo,
    n.mensagem as Mensagem,
    n.tipo as Tipo,
    n.lida as Lida,
    n.created_at as CreatedAt
from ged.protocolo_notificacao n
left join ged.protocolo p on p.id = n.protocolo_id and p.tenant_id = n.tenant_id
where n.tenant_id = @TenantId
  and n.reg_status = 'A'
  and (
        n.usuario_id = @UserId
        or n.setor_id = any(@Setores)
        or @IsAdmin = true
      )
order by n.created_at desc
limit 100;", new
        {
            TenantId,
            UserId,
            Setores = setores,
            IsAdmin = IsAdminOrGestor()
        });

        return View("~/Views/ProtocoloMelhorias/Notificacoes.cshtml", rows.ToList());
    }

    [HttpPost("MarcarNotificacaoLida")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarNotificacaoLida(Guid id)
    {
        using var db = await OpenAsync();

        await db.ExecuteAsync(@"
update ged.protocolo_notificacao
set lida = true,
    lida_at = now()
where tenant_id = @TenantId
  and id = @Id;", new { TenantId, Id = id });

        return RedirectToAction(nameof(Notificacoes));
    }

    [HttpPost("Receber")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receber(Guid protocoloId)
    {
        return await ExecutarAcaoProtocoloAsync(
            protocoloId,
            novoStatus: "RECEBIDO",
            acao: "RECEBIMENTO",
            justificativa: "Protocolo recebido formalmente pelo setor.",
            exigeJustificativa: false);
    }

    [HttpPost("Devolver")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Devolver(Guid protocoloId, Guid setorDestinoId, string justificativa)
    {
        if (string.IsNullOrWhiteSpace(justificativa))
        {
            TempData["erro"] = "Informe a justificativa da devolução.";
            return RedirectToAction("Details", "Protocolo", new { id = protocoloId });
        }

        using var db = await OpenAsync();

        var protocolo = await GetProtocoloBasicoAsync(db, protocoloId);
        if (protocolo == null) return NotFound();

        if (!await PodeOperarAsync(db, protocolo.SetorAtualId))
        {
            TempData["erro"] = "Você não possui permissão para devolver este protocolo.";
            return RedirectToAction("Details", "Protocolo", new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();

        try
        {
            var setorAtualNome = await GetSetorNomeAsync(db, protocolo.SetorAtualId);
            var setorDestinoNome = await GetSetorNomeAsync(db, setorDestinoId);

            await db.ExecuteAsync(@"
update ged.protocolo
set setor_atual_id = @SetorDestinoId,
    status = 'DEVOLVIDO',
    devolvido_por = @UserId,
    devolvido_por_nome = @UserName,
    data_devolucao = now(),
    justificativa_devolucao = @Justificativa,
    updated_at = now(),
    updated_by = @UserId
where tenant_id = @TenantId
  and id = @ProtocoloId;", new
            {
                TenantId,
                ProtocoloId = protocoloId,
                SetorDestinoId = setorDestinoId,
                UserId,
                UserName = UserNameSafe,
                Justificativa = justificativa
            }, tx);

            await RegistrarMovimentoAsync(db, tx, protocoloId, protocolo.SetorAtualId, setorDestinoId, "DEVOLUCAO", protocolo.Status, "DEVOLVIDO", justificativa, setorAtualNome, setorDestinoNome);
            await NotificarSetorAsync(db, tx, protocoloId, setorDestinoId, "Protocolo devolvido", $"O protocolo foi devolvido: {justificativa}", "DEVOLUCAO");

            tx.Commit();
            TempData["ok"] = "Protocolo devolvido com sucesso.";
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return RedirectToAction("Index", "Protocolo");
    }

    [HttpPost("SolicitarComplementacao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SolicitarComplementacao(Guid protocoloId, string justificativa)
    {
        return await ExecutarAcaoProtocoloAsync(
            protocoloId,
            novoStatus: "AGUARDANDO_COMPLEMENTACAO",
            acao: "SOLICITACAO_COMPLEMENTACAO",
            justificativa: justificativa,
            exigeJustificativa: true);
    }

    [HttpPost("Cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(Guid protocoloId, string justificativa)
    {
        if (!IsAdminOrGestor())
            return Forbid();

        return await ExecutarAcaoProtocoloAsync(
            protocoloId,
            novoStatus: "CANCELADO",
            acao: "CANCELAMENTO",
            justificativa: justificativa,
            exigeJustificativa: true);
    }

    [HttpPost("Reabrir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reabrir(Guid protocoloId, string justificativa)
    {
        if (!IsAdminOrGestor())
            return Forbid();

        using var db = await OpenAsync();

        var protocolo = await GetProtocoloBasicoAsync(db, protocoloId);
        if (protocolo == null) return NotFound();

        if (string.IsNullOrWhiteSpace(justificativa))
        {
            TempData["erro"] = "Informe a justificativa de reabertura.";
            return RedirectToAction("Details", "Protocolo", new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();

        try
        {
            await db.ExecuteAsync(@"
update ged.protocolo
set status_anterior_reabertura = status,
    status = 'EM_TRAMITACAO',
    reaberto_por = @UserId,
    reaberto_por_nome = @UserName,
    data_reabertura = now(),
    justificativa_reabertura = @Justificativa,
    updated_at = now(),
    updated_by = @UserId
where tenant_id = @TenantId
  and id = @ProtocoloId;", new
            {
                TenantId,
                ProtocoloId = protocoloId,
                UserId,
                UserName = UserNameSafe,
                Justificativa = justificativa
            }, tx);

            await RegistrarMovimentoAsync(db, tx, protocoloId, protocolo.SetorAtualId, protocolo.SetorAtualId, "REABERTURA", protocolo.Status, "EM_TRAMITACAO", justificativa);
            await NotificarSetorAsync(db, tx, protocoloId, protocolo.SetorAtualId, "Protocolo reaberto", $"O protocolo foi reaberto: {justificativa}", "REABERTURA");

            tx.Commit();
            TempData["ok"] = "Protocolo reaberto com sucesso.";
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return RedirectToAction("Details", "Protocolo", new { id = protocoloId });
    }

    private async Task<IActionResult> ExecutarAcaoProtocoloAsync(Guid protocoloId, string novoStatus, string acao, string justificativa, bool exigeJustificativa)
    {
        if (exigeJustificativa && string.IsNullOrWhiteSpace(justificativa))
        {
            TempData["erro"] = "Informe uma justificativa.";
            return RedirectToAction("Details", "Protocolo", new { id = protocoloId });
        }

        using var db = await OpenAsync();

        var protocolo = await GetProtocoloBasicoAsync(db, protocoloId);
        if (protocolo == null) return NotFound();

        if (!await PodeOperarAsync(db, protocolo.SetorAtualId) && !IsAdminOrGestor())
        {
            TempData["erro"] = "Você não possui permissão para executar esta ação.";
            return RedirectToAction("Details", "Protocolo", new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();

        try
        {
            var extra = novoStatus switch
            {
                "RECEBIDO" => "recebido_por = @UserId, recebido_por_nome = @UserName, data_recebimento = now(),",
                "AGUARDANDO_COMPLEMENTACAO" => "solicitado_complementacao_por = @UserId, solicitado_complementacao_por_nome = @UserName, data_solicitacao_complementacao = now(), justificativa_complementacao = @Justificativa,",
                "CANCELADO" => "cancelado_por = @UserId, cancelado_por_nome = @UserName, data_cancelamento = now(), justificativa_cancelamento = @Justificativa,",
                _ => ""
            };

            var sql = $@"
update ged.protocolo
set {extra}
    status = @NovoStatus,
    updated_at = now(),
    updated_by = @UserId
where tenant_id = @TenantId
  and id = @ProtocoloId;";

            await db.ExecuteAsync(sql, new
            {
                TenantId,
                ProtocoloId = protocoloId,
                NovoStatus = novoStatus,
                UserId,
                UserName = UserNameSafe,
                Justificativa = justificativa
            }, tx);

            await RegistrarMovimentoAsync(db, tx, protocoloId, protocolo.SetorAtualId, protocolo.SetorAtualId, acao, protocolo.Status, novoStatus, justificativa);
            await NotificarSetorAsync(db, tx, protocoloId, protocolo.SetorAtualId, $"Protocolo - {acao}", justificativa ?? acao, acao);

            tx.Commit();
            TempData["ok"] = "Ação realizada com sucesso.";
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return RedirectToAction("Details", "Protocolo", new { id = protocoloId });
    }

    private async Task<ProtocoloComprovanteVM?> MontarComprovanteAsync(Guid id)
    {
        using var db = await OpenAsync();

        var vm = await db.QuerySingleOrDefaultAsync<ProtocoloComprovanteVM>(@"
select
    p.id as Id,
    p.numero as Numero,
    p.created_at as CreatedAt,
    p.data_abertura as DataAbertura,
    p.especie as Especie,
    p.tipo_solicitacao as TipoSolicitacao,
    p.procedencia as Procedencia,
    p.origem_pedido as OrigemPedido,
    p.assunto as Assunto,
    p.interessado as Interessado,
    p.cpf_cnpj as CpfCnpj,
    p.solicitante_nome as SolicitanteNome,
    so.nome as SetorOrigem,
    sa.nome as SetorAtual,
    p.criado_por_nome as CriadoPorNome,
    p.status as Status,
    p.data_prazo as DataPrazo,
    (
        select count(*)
        from ged.protocolo_documento d
        where d.tenant_id = p.tenant_id
          and d.protocolo_id = p.id
          and d.reg_status = 'A'
    ) as TotalAnexos
from ged.protocolo p
left join ged.protocolo_setor so on so.id = p.setor_origem_id
left join ged.protocolo_setor sa on sa.id = p.setor_atual_id
where p.tenant_id = @TenantId
  and p.id = @Id
  and p.reg_status = 'A';", new { TenantId, Id = id });

        if (vm == null) return null;

        vm.Anexos = (await db.QueryAsync<ProtocoloComprovanteAnexoVM>(@"
select nome_arquivo as NomeArquivo,
       setor_nome as SetorNome,
       anexado_por_nome as AnexadoPorNome,
       created_at as CreatedAt
from ged.protocolo_documento
where tenant_id = @TenantId
  and protocolo_id = @Id
  and reg_status = 'A'
order by created_at;", new { TenantId, Id = id })).ToList();

        vm.Movimentos = (await db.QueryAsync<ProtocoloComprovanteMovimentoVM>(@"
select acao as Acao,
       setor_origem_nome as SetorOrigemNome,
       setor_destino_nome as SetorDestinoNome,
       usuario_nome as UsuarioNome,
       justificativa as Justificativa,
       data_tramitacao as DataTramitacao
from ged.protocolo_tramitacao
where tenant_id = @TenantId
  and protocolo_id = @Id
  and reg_status = 'A'
order by data_tramitacao;", new { TenantId, Id = id })).ToList();

        return vm;
    }

    private async Task<ProtocoloBasico?> GetProtocoloBasicoAsync(IDbConnection db, Guid protocoloId)
    {
        return await db.QuerySingleOrDefaultAsync<ProtocoloBasico>(@"
select id as Id,
       status as Status,
       setor_atual_id as SetorAtualId
from ged.protocolo
where tenant_id = @TenantId
  and id = @Id
  and reg_status = 'A';", new { TenantId, Id = protocoloId });
    }

    private async Task<bool> PodeOperarAsync(IDbConnection db, Guid? setorId)
    {
        if (setorId == null) return false;
        if (IsAdminOrGestor()) return true;

        if (UserId == null) return false;

        return await db.ExecuteScalarAsync<bool>(@"
select exists (
    select 1
    from ged.protocolo_usuario_setor
    where tenant_id = @TenantId
      and usuario_id = @UserId
      and setor_id = @SetorId
      and ativo = true
      and reg_status = 'A'
);", new { TenantId, UserId, SetorId = setorId });
    }

    private async Task<Guid[]> GetSetoresUsuarioIdsAsync(IDbConnection db)
    {
        if (UserId == null) return Array.Empty<Guid>();

        var rows = await db.QueryAsync<Guid>(@"
select setor_id
from ged.protocolo_usuario_setor
where tenant_id = @TenantId
  and usuario_id = @UserId
  and ativo = true
  and reg_status = 'A';", new { TenantId, UserId });

        return rows.ToArray();
    }

    private async Task<List<SelectListItem>> GetSetoresSelectAsync(IDbConnection db)
    {
        var rows = await db.QueryAsync<(Guid Id, string Nome)>(@"
select id, nome
from ged.protocolo_setor
where tenant_id = @TenantId
  and ativo = true
  and reg_status = 'A'
order by ordem, nome;", new { TenantId });

        return rows.Select(x => new SelectListItem
        {
            Value = x.Id.ToString(),
            Text = x.Nome
        }).ToList();
    }

    private async Task<string?> GetSetorNomeAsync(IDbConnection db, Guid? setorId)
    {
        if (setorId == null) return null;

        return await db.ExecuteScalarAsync<string?>(@"
select nome
from ged.protocolo_setor
where tenant_id = @TenantId
  and id = @Id;", new { TenantId, Id = setorId });
    }

    private async Task RegistrarMovimentoAsync(
        IDbConnection db,
        IDbTransaction tx,
        Guid protocoloId,
        Guid? setorOrigemId,
        Guid? setorDestinoId,
        string acao,
        string? statusAnterior,
        string? statusNovo,
        string? justificativa,
        string? setorOrigemNome = null,
        string? setorDestinoNome = null)
    {
        setorOrigemNome ??= await GetSetorNomeAsync(db, setorOrigemId);
        setorDestinoNome ??= await GetSetorNomeAsync(db, setorDestinoId);

        await db.ExecuteAsync(@"
insert into ged.protocolo_tramitacao
(id, tenant_id, protocolo_id, setor_origem_id, setor_origem_nome, setor_destino_id, setor_destino_nome,
 usuario_id, usuario_nome, acao, status_anterior, status_novo, justificativa, data_tramitacao, ip, user_agent, reg_status)
values
(gen_random_uuid(), @TenantId, @ProtocoloId, @SetorOrigemId, @SetorOrigemNome, @SetorDestinoId, @SetorDestinoNome,
 @UserId, @UserName, @Acao, @StatusAnterior, @StatusNovo, @Justificativa, now(), @Ip, @UserAgent, 'A');", new
        {
            TenantId,
            ProtocoloId = protocoloId,
            SetorOrigemId = setorOrigemId,
            SetorOrigemNome = setorOrigemNome,
            SetorDestinoId = setorDestinoId,
            SetorDestinoNome = setorDestinoNome,
            UserId,
            UserName = UserNameSafe,
            Acao = acao,
            StatusAnterior = statusAnterior,
            StatusNovo = statusNovo,
            Justificativa = justificativa,
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString()
        }, tx);
    }

    private async Task NotificarSetorAsync(IDbConnection db, IDbTransaction tx, Guid protocoloId, Guid? setorId, string titulo, string mensagem, string tipo)
    {
        if (setorId == null) return;

        await db.ExecuteAsync(@"
insert into ged.protocolo_notificacao
(id, tenant_id, protocolo_id, setor_id, titulo, mensagem, tipo, lida, created_at, reg_status)
values
(gen_random_uuid(), @TenantId, @ProtocoloId, @SetorId, @Titulo, @Mensagem, @Tipo, false, now(), 'A');", new
        {
            TenantId,
            ProtocoloId = protocoloId,
            SetorId = setorId,
            Titulo = titulo,
            Mensagem = mensagem,
            Tipo = tipo
        }, tx);
    }

    private bool IsAdminOrGestor()
    {
        return User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Gestor) || User.IsInRole(AppRoles.Auditor);
    }

    private sealed class ProtocoloBasico
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public Guid? SetorAtualId { get; set; }
    }
}
