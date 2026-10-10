using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Security;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Ged.Protocols;

public sealed class ProtocoloCentralService : IProtocoloCentralService
{
    private const string ClosedSql = "('FINALIZADO','ENCERRADO','ARQUIVADO','CANCELADO','DEFERIDO','INDEFERIDO')";
    private readonly IDbConnectionFactory _db;
    private readonly IAbacAuthorizationService _abac;
    private readonly InovaGed.Application.Retention.IRetentionJobRepository _retentionJobs;
    private readonly ILogger<ProtocoloCentralService> _logger;

    public ProtocoloCentralService(IDbConnectionFactory db, ILogger<ProtocoloCentralService> logger, IAbacAuthorizationService abac, InovaGed.Application.Retention.IRetentionJobRepository? retentionJobs = null)
    {
        _db = db;
        _abac = abac;
        _retentionJobs = retentionJobs ?? new InovaGed.Infrastructure.Retention.RetentionJobRepository(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<InovaGed.Infrastructure.Retention.RetentionJobRepository>.Instance);
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProtocoloSetorOption>> ListSectorsAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProtocoloSetorOption>(new CommandDefinition("""
select id as Id, nome as Nome, sigla as Sigla
from ged.protocolo_setor
where tenant_id=@TenantId and reg_status='A' and ativo=true
order by ordem, nome;
""", new { TenantId = tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<ProtocoloCentralPage> SearchAsync(ProtocoloCentralQuery query, CancellationToken ct)
    {
        query.Visao = NormalizeVisao(query.Visao);
        query.Page = query.Page <= 0 ? 1 : query.Page;
        query.PageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 100);
        var page = new ProtocoloCentralPage { Query = query, Page = query.Page, PageSize = query.PageSize, Setores = (await ListSectorsAsync(query.TenantId, ct)).ToList() };
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var grants = await LoadGrantsAsync(conn, null, query.TenantId, query.UserId, ct);
            if (!query.CanSeeAll && grants.Count == 0)
            {
                page.SemVinculoSetor = true;
                page.Error = "Seu usuário não está vinculado a um setor ativo. A fila permanece vazia.";
                return page;
            }

            var setores = grants.Select(g => g.SetorId).ToArray();
            var candidates = await conn.QueryAsync<Guid>(new CommandDefinition(
                "select distinct ged_document_id from ged.protocolo_documento_ged where tenant_id=@TenantId and reg_status='A'",
                new { query.TenantId }, cancellationToken: ct));
            var visibleDocuments = (await _abac.FilterDocumentsAsync(query.TenantId, query.UserId, candidates.ToArray(), "VIEW", ct)).ToArray();
            page.Counts = await CountAsync(conn, query, setores, visibleDocuments, ct);
            var parameters = BuildParameters(query, setores, visibleDocuments);
            var where = BuildWhere(query, setores);
            page.Total = await conn.ExecuteScalarAsync<int>(new CommandDefinition($"select count(*)::int from ged.protocolo p where {where}", parameters, cancellationToken: ct));
            parameters.Add("Limit", query.PageSize);
            parameters.Add("Offset", (query.Page - 1) * query.PageSize);
            var sql = $"""
select p.id as Id, p.numero as Numero, p.assunto as Assunto, p.interessado as Interessado,
       p.tipo_solicitacao as Tipo, p.status as Status, p.situacao_custodia as SituacaoCustodia,
       sa.nome as Setor, coalesce(p.recebido_por_nome, p.criado_por_nome) as Responsavel,
       p.setor_atual_id as SetorAtualId, p.data_prazo as Prazo,
       (p.data_prazo is not null and p.data_prazo < now() and upper(p.status) not in {ClosedSql}) as PrazoVencido,
       m.id as MovimentoId, m.situacao_movimentacao as SituacaoMovimentacao, m.data_tramitacao as UltimaMovimentacao,
       m.setor_destino_id as MovimentoDestinoId, m.setor_origem_id as MovimentoOrigemId,
       coalesce(m.despacho, m.observacao) as Observacao,
       (
         select string_agg(distinct coalesce(d.code, d.title), ', ')
         from ged.protocolo_documento_ged g
         join ged.document d on d.tenant_id=g.tenant_id and d.id=g.ged_document_id
         where g.tenant_id=p.tenant_id and g.protocolo_id=p.id and g.reg_status='A' and d.id=any(@VisibleDocuments)
       ) as CodigoDocumental
from ged.protocolo p
left join ged.protocolo_setor sa on sa.tenant_id=p.tenant_id and sa.id=p.setor_atual_id
left join lateral (
    select t.id, t.situacao_movimentacao, t.data_tramitacao, t.setor_destino_id, t.setor_origem_id, t.despacho, t.observacao
    from ged.protocolo_tramitacao t
    where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A'
      and t.situacao_movimentacao is not null
    order by t.ativa desc, t.data_tramitacao desc
    limit 1
) m on true
where {where}
order by coalesce(m.data_tramitacao, p.created_at) desc
offset @Offset limit @Limit;
""";
            page.Rows = (await conn.QueryAsync<ProtocoloCentralRow>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
            foreach (var row in page.Rows) ApplyFlags(row, grants, query.CanSeeAll);
            if (query.SelecionadoId.HasValue)
                page.Selecionado = await LoadDetailAsync(conn, query.TenantId, query.SelecionadoId.Value, grants, query.CanSeeAll, visibleDocuments, ct);
            return page;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Falha ao consultar a central de tramitação. Tenant={TenantId} SqlState={SqlState}", query.TenantId, ex.SqlState);
            page.Error = "Não foi possível carregar a central de tramitação. Confirme se a migration de custódia foi aplicada. Atendimento: " + Guid.NewGuid().ToString("N")[..12];
            page.Rows = new();
            return page;
        }
    }

    public Task<ProtocoloCommandResult> ForwardAsync(ProtocoloActor actor, Guid protocoloId, Guid? documentoId, Guid destinoSetorId, string? despacho, string? observacao, string? idempotencyKey, DateTime? prazo, Guid? responsavelId, string? entregueA, CancellationToken ct, Guid? minutaId = null)
        => MutateAsync(actor, async (conn, tx) =>
        {
            if (destinoSetorId == Guid.Empty) return ProtocoloCommandResult.Fail("Informe o setor de destino.");
            var destino = await SectorAsync(conn, tx, actor.TenantId, destinoSetorId, ct);
            if (destino is null) return ProtocoloCommandResult.Fail("O setor de destino não existe, está inativo ou pertence a outro tenant.");
            if (responsavelId.HasValue && !await UserInSectorAsync(conn, tx, actor.TenantId, responsavelId.Value, destinoSetorId, ct))
                return ProtocoloCommandResult.Fail("O responsável informado não está ativo no setor de destino.");
            var existingKey = await FindIdempotentAsync(conn, tx, actor.TenantId, idempotencyKey, ct);
            if (existingKey.HasValue) return ProtocoloCommandResult.Ok("Encaminhamento já registrado.", existingKey, true);

            var protocol = await LockProtocolAsync(conn, tx, actor.TenantId, protocoloId, ct);
            if (protocol is null) return ProtocoloCommandResult.Fail("Protocolo não encontrado.");
            if (ProtocolCustodyRules.IsClosed(protocol.Status))
                return ProtocoloCommandResult.Fail("O processo está encerrado. Reabra com justificativa antes de tramitar.");
            var grants = await LoadGrantsAsync(conn, tx, actor.TenantId, actor.UserId, ct);
            var holder = protocol.SetorAtualId ?? protocol.SetorOrigemId;
            if (!ProtocolCustodyRules.CanAct(actor.CanSeeAll, grants.Any(g => g.SetorId == holder && g.PodeTramitar), true))
                return ProtocoloCommandResult.Fail("Você não tem direito de tramitar a custódia atual deste protocolo.");

            var effectiveDespacho = despacho;
            ProtocolDraftForDispatch? draft = null;
            if (minutaId.HasValue)
            {
                draft = await conn.QuerySingleOrDefaultAsync<ProtocolDraftForDispatch>(new CommandDefinition("""
select id as Id, protocolo_id as ProtocoloId, setor_id as SetorId, titulo as Titulo,
       conteudo as Conteudo, status as Status, versao as Versao
from ged.protocolo_minuta
where tenant_id=@TenantId and id=@MinutaId and reg_status='A'
for update;
""", new { actor.TenantId, MinutaId = minutaId }, tx, cancellationToken: ct));
                if (draft is null || draft.ProtocoloId != protocoloId)
                    return ProtocoloCommandResult.Fail("Minuta não encontrada para este protocolo.");
                if (!string.Equals(draft.Status, "CONFIRMADA", StringComparison.Ordinal))
                    return ProtocoloCommandResult.Fail("Confirme a minuta antes de encaminhá-la.");
                if (draft.SetorId != holder)
                    return ProtocoloCommandResult.Fail("A minuta pertence a outro setor e não pode ser encaminhada pela custódia atual.");
                effectiveDespacho = draft.Conteudo;
            }

            var active = await LockActiveAsync(conn, tx, actor.TenantId, protocoloId, documentoId, ct);
            if (active is not null)
            {
                if (ProtocolCustodyRules.IsSameDestinationRepeat(active.Situacao, active.DestinoId, destinoSetorId) && !minutaId.HasValue)
                    return ProtocoloCommandResult.Ok("Este encaminhamento já está pendente de recebimento.", active.Id, true);
                var block = ProtocolCustodyRules.SecondForwardBlock(active.Situacao, active.DestinoId, destinoSetorId);
                if (block is not null) return ProtocoloCommandResult.Fail(block);
                if (minutaId.HasValue)
                    return ProtocoloCommandResult.Fail("Há um encaminhamento pendente. A minuta permanece confirmada e não foi encaminhada.");
            }

            var id = await InsertMovementAsync(conn, tx, actor, protocol, documentoId, destino.Id, destino.Nome, "TRAMITACAO", ProtocolCustodyRules.Waiting, effectiveDespacho, observacao, null, idempotencyKey, prazo, responsavelId, entregueA, true, ct);
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo
set status=case when upper(status) in ('RASCUNHO','ABERTO') then 'EM_TRAMITACAO' else status end,
    situacao_custodia=@Situacao, updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@Id;
""", new { Situacao = ProtocolCustodyRules.Waiting, actor.UserId, actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
            await UpsertParticipantAsync(conn, tx, actor.TenantId, protocoloId, destinoSetorId, false, ct);
            if (draft is not null)
            {
                var updated = await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo_minuta
set status='ENCAMINHADA', encaminhada_por=@UserId, encaminhada_em=now(),
    movimento_id=@MovimentoId, atualizado_por=@UserId, atualizado_por_nome=@UserName,
    updated_at=now()
where tenant_id=@TenantId and id=@MinutaId and status='CONFIRMADA' and versao=@Versao;
""", new { actor.UserId, actor.UserName, MovimentoId = id, actor.TenantId, MinutaId = draft.Id, draft.Versao }, tx, cancellationToken: ct));
                if (updated != 1)
                    throw new InvalidOperationException("A minuta mudou durante o encaminhamento; nenhuma tramitação foi confirmada.");
                await WriteDraftHistoryAsync(conn, tx, actor, protocoloId, draft, "ENCAMINHADA", new { movementId = id, destinationSectorId = destinoSetorId }, ct);
            }
            return ProtocoloCommandResult.Ok($"Protocolo encaminhado para {destino.Nome}. O destino ainda precisa receber.", id);
        }, ct);

    public Task<ProtocoloCommandResult> ReceiveAsync(ProtocoloActor actor, Guid? movimentoId, Guid? protocoloId, CancellationToken ct)
        => CompleteMovementAsync(actor, movimentoId, protocoloId, ProtocolCustodyRules.Waiting, false, ct);

    public Task<ProtocoloCommandResult> ConfirmReturnAsync(ProtocoloActor actor, Guid? movimentoId, Guid? protocoloId, CancellationToken ct)
        => CompleteMovementAsync(actor, movimentoId, protocoloId, ProtocolCustodyRules.ReturnPending, true, ct);

    public Task<ProtocoloCommandResult> ReturnAsync(ProtocoloActor actor, Guid protocoloId, string? observacao, string? idempotencyKey, CancellationToken ct)
        => MutateAsync(actor, async (conn, tx) =>
        {
            var existingKey = await FindIdempotentAsync(conn, tx, actor.TenantId, idempotencyKey, ct);
            if (existingKey.HasValue) return ProtocoloCommandResult.Ok("Devolução já registrada.", existingKey, true);
            var protocol = await LockProtocolAsync(conn, tx, actor.TenantId, protocoloId, ct);
            if (protocol is null) return ProtocoloCommandResult.Fail("Protocolo não encontrado.");
            if (ProtocolCustodyRules.IsClosed(protocol.Status))
                return ProtocoloCommandResult.Fail("O processo está encerrado. Reabra com justificativa antes de devolver.");
            var grants = await LoadGrantsAsync(conn, tx, actor.TenantId, actor.UserId, ct);
            if (!ProtocolCustodyRules.CanAct(actor.CanSeeAll, grants.Any(g => g.SetorId == protocol.SetorAtualId && g.PodeTramitar), true))
                return ProtocoloCommandResult.Fail("Você não tem direito de devolver a custódia atual.");
            var active = await LockActiveAsync(conn, tx, actor.TenantId, protocoloId, null, ct);
            if (active is not null)
                return ProtocoloCommandResult.Fail("Há movimentação pendente. Receba, confirme o retorno ou estorne antes de devolver.");
            var back = await conn.QuerySingleOrDefaultAsync<(Guid? Id, string? Nome)>(new CommandDefinition("""
select setor_origem_id as Id, setor_origem_nome as Nome
from ged.protocolo_tramitacao
where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A' and situacao_movimentacao='RECEBIDA'
order by data_tramitacao desc
limit 1;
""", new { actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
            if (!back.Id.HasValue) back = (protocol.SetorOrigemId, null);
            var destino = back.Id.HasValue ? await SectorAsync(conn, tx, actor.TenantId, back.Id.Value, ct) : null;
            if (destino is null) return ProtocoloCommandResult.Fail("Não foi possível resolver o setor de retorno no servidor.");
            if (protocol.SetorAtualId == destino.Id)
                return ProtocoloCommandResult.Fail("A custódia já está no setor de retorno.");
            var id = await InsertMovementAsync(conn, tx, actor, protocol, null, destino.Id, destino.Nome, "DEVOLUCAO", ProtocolCustodyRules.ReturnPending, null, observacao, null, idempotencyKey, null, null, null, true, ct);
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo set situacao_custodia=@Situacao, updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@Id;
""", new { Situacao = ProtocolCustodyRules.ReturnPending, actor.UserId, actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
            await UpsertParticipantAsync(conn, tx, actor.TenantId, protocoloId, destino.Id, false, ct);
            return ProtocoloCommandResult.Ok($"Devolução encaminhada para {destino.Nome}. O retorno ainda precisa ser confirmado.", id);
        }, ct);

    public Task<ProtocoloCommandResult> ReverseAsync(ProtocoloActor actor, Guid protocoloId, string justificativa, CancellationToken ct)
        => MutateAsync(actor, async (conn, tx) =>
        {
            if (string.IsNullOrWhiteSpace(justificativa)) return ProtocoloCommandResult.Fail("Informe a justificativa do estorno.");
            var protocol = await LockProtocolAsync(conn, tx, actor.TenantId, protocoloId, ct);
            if (protocol is null) return ProtocoloCommandResult.Fail("Protocolo não encontrado.");
            var last = await conn.QuerySingleOrDefaultAsync<ActiveMove>(new CommandDefinition("""
select id as Id, situacao_movimentacao as Situacao, setor_origem_id as OrigemId, setor_destino_id as DestinoId, status_anterior as StatusAnterior, ativa as Ativa
from ged.protocolo_tramitacao
where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A'
  and acao in ('TRAMITACAO','DEVOLUCAO')
order by data_tramitacao desc
limit 1
for update;
""", new { actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
            if (last is null) return ProtocoloCommandResult.Fail("Não há movimentação de custódia para estornar.");
            var block = ProtocolCustodyRules.ReverseBlock(last.Situacao);
            if (block is not null) return ProtocoloCommandResult.Fail(block);
            var grants = await LoadGrantsAsync(conn, tx, actor.TenantId, actor.UserId, ct);
            if (!ProtocolCustodyRules.CanAct(actor.CanSeeAll, grants.Any(g => g.SetorId == last.OrigemId && g.PodeTramitar), true))
                return ProtocoloCommandResult.Fail("Você não tem direito de estornar esta movimentação.");
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo_tramitacao
set situacao_movimentacao='ESTORNADA', ativa=false, justificativa=coalesce(justificativa,'') 
where tenant_id=@TenantId and id=@Id;
""", new { actor.TenantId, last.Id }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocolo_tramitacao(
    tenant_id, protocolo_id, setor_origem_id, setor_destino_id, usuario_id, usuario_nome, acao,
    status_anterior, status_novo, justificativa, situacao_movimentacao, estorno_de_id, ativa, ip, user_agent)
select tenant_id, protocolo_id, setor_origem_id, setor_destino_id, @UserId, @UserName, 'ESTORNO',
    status_novo, status_anterior, @Justificativa, 'ESTORNADA', id, false, @Ip, @Ua
from ged.protocolo_tramitacao where tenant_id=@TenantId and id=@Id;
""", new { actor.TenantId, last.Id, actor.UserId, UserName = actor.UserName, Justificativa = justificativa.Trim(), actor.Ip, Ua = actor.UserAgent }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo
set status=coalesce(@StatusAnterior, status),
    situacao_custodia='EM_CUSTODIA', updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@ProtocoloId;
""", new { actor.TenantId, ProtocoloId = protocoloId, actor.UserId, last.StatusAnterior }, tx, cancellationToken: ct));
            return ProtocoloCommandResult.Ok("Estorno registrado. A movimentação original permanece no histórico.");
        }, ct);

    public Task<ProtocoloCommandResult> ReopenAsync(ProtocoloActor actor, Guid protocoloId, string justificativa, CancellationToken ct)
        => MutateAsync(actor, async (conn, tx) =>
        {
            if (!actor.IsFullAdmin && !actor.CanSeeAll) return ProtocoloCommandResult.Fail("Reabertura exige autorização administrativa.");
            if (string.IsNullOrWhiteSpace(justificativa)) return ProtocoloCommandResult.Fail("Informe a justificativa da reabertura.");
            var protocol = await LockProtocolAsync(conn, tx, actor.TenantId, protocoloId, ct);
            if (protocol is null) return ProtocoloCommandResult.Fail("Protocolo não encontrado.");
            if (!ProtocolCustodyRules.IsClosed(protocol.Status)) return ProtocoloCommandResult.Ok("O processo já está aberto.", idempotent: true);
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo
set status='ABERTO', situacao_custodia='EM_CUSTODIA', data_encerramento=null,
    reaberto_por=@UserId, reaberto_por_nome=@UserName, data_reabertura=now(),
    justificativa_reabertura=@Justificativa, status_anterior_reabertura=@Anterior,
    updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@Id;
""", new { actor.TenantId, Id = protocoloId, actor.UserId, UserName = actor.UserName, Justificativa = justificativa.Trim(), Anterior = protocol.Status }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocolo_tramitacao(
    tenant_id, protocolo_id, setor_origem_id, setor_destino_id, usuario_id, usuario_nome, acao,
    status_anterior, status_novo, justificativa, situacao_movimentacao, ativa, ip, user_agent)
values (@TenantId, @Id, @Setor, @Setor, @UserId, @UserName, 'REABERTURA', @Anterior, 'ABERTO', @Justificativa, null, false, @Ip, @Ua);
""", new { actor.TenantId, Id = protocoloId, Setor = protocol.SetorAtualId, actor.UserId, UserName = actor.UserName, Anterior = protocol.Status, Justificativa = justificativa.Trim(), actor.Ip, Ua = actor.UserAgent }, tx, cancellationToken: ct));
            return ProtocoloCommandResult.Ok("Processo reaberto. A temporalidade e os documentos não foram alterados.");
        }, ct);

    public Task<ProtocoloCommandResult> CloseAsync(ProtocoloActor actor, Guid protocoloId, string justificativa, string? decisao, CancellationToken ct)
        => MutateAsync(actor, async (conn, tx) =>
        {
            if (string.IsNullOrWhiteSpace(justificativa)) return ProtocoloCommandResult.Fail("Informe a justificativa do encerramento.");
            var protocol = await LockProtocolAsync(conn, tx, actor.TenantId, protocoloId, ct);
            if (protocol is null) return ProtocoloCommandResult.Fail("Protocolo não encontrado.");
            if (ProtocolCustodyRules.IsClosed(protocol.Status)) return ProtocoloCommandResult.Ok("O processo já está encerrado.", idempotent: true);

            var grants = await LoadGrantsAsync(conn, tx, actor.TenantId, actor.UserId, ct);
            if (!ProtocolCustodyRules.CanAct(actor.CanSeeAll || actor.IsFullAdmin, grants.Any(g => g.SetorId == protocol.SetorAtualId && (g.PodeDecidir || g.PodeTramitar)), true))
                return ProtocoloCommandResult.Fail("Seu usuário não possui permissão para encerrar este processo.");

            var linkedDocIds = (await conn.QueryAsync<Guid>(new CommandDefinition(
                "select distinct ged_document_id from ged.protocolo_documento_ged where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A'",
                new { actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct))).ToList();
            if (linkedDocIds.Count > 0 && !actor.CanSeeAll && !actor.IsFullAdmin)
            {
                var allowedDocs = await _abac.FilterDocumentsAsync(actor.TenantId, actor.UserId, linkedDocIds, "VIEW", ct);
                if (allowedDocs.Count < linkedDocIds.Count)
                    return ProtocoloCommandResult.Fail("Usuário não possui autorização documental sobre todos os documentos vinculados ao processo.");
            }

            var pending = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from ged.protocolo_tramitacao where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A' and ativa=true and situacao_movimentacao in ('AGUARDANDO_RECEBIMENTO','DEVOLUCAO_PENDENTE')",
                new { actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
            if (pending > 0) return ProtocoloCommandResult.Fail("Há movimentação pendente. Receba, confirme o retorno ou estorne antes de encerrar.");

            var newStatus = string.IsNullOrWhiteSpace(decisao) ? "FINALIZADO" : decisao.Trim().ToUpperInvariant();
            if (newStatus is not ("FINALIZADO" or "DEFERIDO" or "INDEFERIDO")) newStatus = "FINALIZADO";

            await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo
set status=@NewStatus, situacao_custodia='ENCERRADO', data_encerramento=now(),
    justificativa_encerramento=@Justificativa, updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@Id;
""", new { actor.TenantId, Id = protocoloId, NewStatus = newStatus, Justificativa = justificativa.Trim(), actor.UserId }, tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocolo_tramitacao(
    tenant_id, protocolo_id, setor_origem_id, setor_destino_id, usuario_id, usuario_nome, acao,
    status_anterior, status_novo, justificativa, despacho, situacao_movimentacao, ativa, ip, user_agent)
values (@TenantId, @Id, @Setor, @Setor, @UserId, @UserName, 'ENCERRAMENTO', @Anterior, @NewStatus, @Justificativa, @Despacho, null, false, @Ip, @Ua);
""", new { actor.TenantId, Id = protocoloId, Setor = protocol.SetorAtualId, actor.UserId, UserName = actor.UserName, Anterior = protocol.Status, NewStatus = newStatus, Justificativa = justificativa.Trim(), Despacho = decisao, actor.Ip, Ua = actor.UserAgent }, tx, cancellationToken: ct));

            await ApplyArchivalEffectAsync(conn, tx, actor.TenantId, protocoloId, protocol.SetorAtualId, actor.UserId, actor.UserName, isEncerramento: true, isArquivamento: false, ct);
            return ProtocoloCommandResult.Ok("Processo encerrado com sucesso.");
        }, ct);

    public Task<ProtocoloCommandResult> ArchiveAsync(ProtocoloActor actor, Guid protocoloId, string justificativa, string? localizacaoFisica, CancellationToken ct)
        => MutateAsync(actor, async (conn, tx) =>
        {
            if (string.IsNullOrWhiteSpace(justificativa)) return ProtocoloCommandResult.Fail("Informe a justificativa do arquivamento.");
            var protocol = await LockProtocolAsync(conn, tx, actor.TenantId, protocoloId, ct);
            if (protocol is null) return ProtocoloCommandResult.Fail("Protocolo não encontrado.");
            if (string.Equals(protocol.Status, "ARQUIVADO", StringComparison.OrdinalIgnoreCase)) return ProtocoloCommandResult.Ok("O processo já está arquivado.", idempotent: true);

            var grants = await LoadGrantsAsync(conn, tx, actor.TenantId, actor.UserId, ct);
            if (!ProtocolCustodyRules.CanAct(actor.CanSeeAll || actor.IsFullAdmin, grants.Any(g => g.SetorId == protocol.SetorAtualId && (g.PodeDecidir || g.PodeTramitar)), true))
                return ProtocoloCommandResult.Fail("Seu usuário não possui permissão para arquivar este processo.");

            var linkedDocIds = (await conn.QueryAsync<Guid>(new CommandDefinition(
                "select distinct ged_document_id from ged.protocolo_documento_ged where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A'",
                new { actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct))).ToList();
            if (linkedDocIds.Count > 0 && !actor.CanSeeAll && !actor.IsFullAdmin)
            {
                var allowedDocs = await _abac.FilterDocumentsAsync(actor.TenantId, actor.UserId, linkedDocIds, "VIEW", ct);
                if (allowedDocs.Count < linkedDocIds.Count)
                    return ProtocoloCommandResult.Fail("Usuário não possui autorização documental sobre todos os documentos vinculados ao processo.");
            }

            var pending = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from ged.protocolo_tramitacao where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A' and ativa=true and situacao_movimentacao in ('AGUARDANDO_RECEBIMENTO','DEVOLUCAO_PENDENTE')",
                new { actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
            if (pending > 0) return ProtocoloCommandResult.Fail("Há movimentação pendente. Receba, confirme o retorno ou estorne antes de arquivar.");

            await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo
set status='ARQUIVADO', situacao_custodia='ARQUIVADO', data_encerramento=coalesce(data_encerramento, now()),
    justificativa_encerramento=@Justificativa, updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@Id;
""", new { actor.TenantId, Id = protocoloId, Justificativa = justificativa.Trim(), actor.UserId }, tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocolo_tramitacao(
    tenant_id, protocolo_id, setor_origem_id, setor_destino_id, usuario_id, usuario_nome, acao,
    status_anterior, status_novo, justificativa, observacao, situacao_movimentacao, ativa, ip, user_agent)
values (@TenantId, @Id, @Setor, @Setor, @UserId, @UserName, 'ARQUIVAMENTO', @Anterior, 'ARQUIVADO', @Justificativa, @Obs, null, false, @Ip, @Ua);
""", new { actor.TenantId, Id = protocoloId, Setor = protocol.SetorAtualId, actor.UserId, UserName = actor.UserName, Anterior = protocol.Status, Justificativa = justificativa.Trim(), Obs = localizacaoFisica, actor.Ip, Ua = actor.UserAgent }, tx, cancellationToken: ct));

            await ApplyArchivalEffectAsync(conn, tx, actor.TenantId, protocoloId, protocol.SetorAtualId, actor.UserId, actor.UserName, isEncerramento: false, isArquivamento: true, ct);
            return ProtocoloCommandResult.Ok("Processo arquivado com sucesso.");
        }, ct);

    private async Task ApplyArchivalEffectAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid protocoloId, Guid? setorId, Guid userId, string userName, bool isEncerramento, bool isArquivamento, CancellationToken ct)
    {
        const string findDocsSql = """
SELECT 
    d.id as DocumentId,
    d.closed_at as ClosedAt,
    d.archived_at as ArchivedAt,
    case
        when d.classification_version_id is not null then pvi.retention_start_event::text
        else cp.retention_start_event::text
    end as StartEvent
FROM ged.protocolo_documento_ged pdg
JOIN ged.document d ON d.tenant_id = pdg.tenant_id AND d.id = pdg.ged_document_id AND d.reg_status = 'A'
LEFT JOIN ged.classification_plan cp ON cp.tenant_id = d.tenant_id AND cp.id = d.classification_id
LEFT JOIN ged.classification_plan_version_item pvi 
    ON pvi.tenant_id = d.tenant_id 
   AND pvi.version_id = d.classification_version_id 
   AND pvi.classification_id = d.classification_id
WHERE pdg.tenant_id = @TenantId 
  AND pdg.protocolo_id = @ProtocoloId 
  AND pdg.reg_status = 'A'
FOR UPDATE OF d;
""";
        var linkedDocs = (await conn.QueryAsync<(Guid DocumentId, DateTime? ClosedAt, DateTime? ArchivedAt, string? StartEvent)>(
            new CommandDefinition(findDocsSql, new { TenantId = tenantId, ProtocoloId = protocoloId }, tx, cancellationToken: ct))).ToList();

        foreach (var docItem in linkedDocs)
        {
            var otherActiveCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
SELECT COUNT(1)
FROM ged.protocolo_documento_ged pdg
JOIN ged.protocolo p ON p.tenant_id = pdg.tenant_id AND p.id = pdg.protocolo_id AND p.reg_status = 'A'
WHERE pdg.tenant_id = @TenantId
  AND pdg.ged_document_id = @DocId
  AND pdg.protocolo_id <> @ProtocoloId
  AND pdg.reg_status = 'A'
  AND UPPER(p.status) NOT IN ('FINALIZADO', 'ENCERRADO', 'ARQUIVADO', 'CANCELADO', 'DEFERIDO', 'INDEFERIDO');
""", new { TenantId = tenantId, DocId = docItem.DocumentId, ProtocoloId = protocoloId }, tx, cancellationToken: ct));

            if (otherActiveCount > 0)
            {
                await conn.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.protocolo_tramitacao(
    tenant_id, protocolo_id, setor_origem_id, setor_destino_id, usuario_id, usuario_nome,
    acao, status_anterior, status_novo, justificativa, observacao, ativa)
VALUES (@TenantId, @ProtocoloId, @Setor, @Setor, @UserId, @UserName, 'EFEITO_ARQUIVISTICO', 'ENCERRADO', 'ENCERRADO',
    'Efeito arquivístico mantido pendente: documento possui outros protocolos ativos vinculados.',
    @DocInfo, false);
""", new { TenantId = tenantId, ProtocoloId = protocoloId, Setor = setorId, UserId = userId, UserName = userName, DocInfo = $"Documento {docItem.DocumentId} vinculado a {otherActiveCount} outro(s) protocolo(s) ativo(s)." }, tx, cancellationToken: ct));
                continue;
            }

            if (isEncerramento && docItem.StartEvent == "ENCERRAMENTO" && !docItem.ClosedAt.HasValue)
            {
                await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document 
SET closed_at = NOW(), updated_at = NOW(), updated_by = @UserId
WHERE tenant_id = @TenantId AND id = @DocId;
""", new { TenantId = tenantId, DocId = docItem.DocumentId, UserId = userId }, tx, cancellationToken: ct));
                await _retentionJobs.EnqueueRecalculateAsync(conn, tx, tenantId, docItem.DocumentId, "PROTOCOL_CLOSED_EVENT", ct);
            }
            else if (isArquivamento && docItem.StartEvent == "ARQUIVAMENTO" && !docItem.ArchivedAt.HasValue)
            {
                await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document 
SET archived_at = NOW(), updated_at = NOW(), updated_by = @UserId
WHERE tenant_id = @TenantId AND id = @DocId;
""", new { TenantId = tenantId, DocId = docItem.DocumentId, UserId = userId }, tx, cancellationToken: ct));
                await _retentionJobs.EnqueueRecalculateAsync(conn, tx, tenantId, docItem.DocumentId, "PROTOCOL_ARCHIVED_EVENT", ct);
            }
        }
    }

    public async Task<ProtocoloBatchPreview> PreviewBatchAsync(ProtocoloActor actor, ProtocoloLoteCommand command, CancellationToken ct)
    {
        var itens = new List<ProtocoloBatchItemResult>();
        string? destinoNome = null;
        string? responsavel = null;
        await using var conn = await _db.OpenAsync(ct);
        if (command.DestinoSetorId.HasValue)
        {
            var setor = await SectorAsync(conn, null, actor.TenantId, command.DestinoSetorId.Value, ct);
            destinoNome = setor?.Nome;
            if (setor is null && RequiresDestination(command.Acao))
                itens.Add(new ProtocoloBatchItemResult { Impedido = true, Mensagem = "Setor de destino inválido para este tenant." });
        }
        if (command.ResponsavelId.HasValue)
        {
            responsavel = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select name from ged.app_user where tenant_id=@TenantId and id=@Id and is_active=true and deleted_at_utc is null", new { actor.TenantId, Id = command.ResponsavelId }, cancellationToken: ct));
            if (responsavel is null)
                itens.Add(new ProtocoloBatchItemResult { Impedido = true, Mensagem = "Responsável inválido ou inativo." });
        }
        foreach (var id in (command.Ids ?? []).Where(x => x != Guid.Empty).Distinct())
        {
            var row = await conn.QuerySingleOrDefaultAsync<(string? Numero, string? Status, string? Situacao, Guid? Destino)>(new CommandDefinition("""
select p.numero, p.status, m.situacao_movimentacao, m.setor_destino_id
from ged.protocolo p
left join ged.protocolo_tramitacao m on m.tenant_id=p.tenant_id and m.protocolo_id=p.id and m.reg_status='A' and m.ativa=true
where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A';
""", new { actor.TenantId, Id = id }, cancellationToken: ct));
            if (row.Numero is null)
            {
                itens.Add(new ProtocoloBatchItemResult { ProtocoloId = id, Impedido = true, Mensagem = "Protocolo inacessível neste tenant." });
                continue;
            }
            var message = PreviewMessage(command, row.Status, row.Situacao, row.Destino);
            itens.Add(new ProtocoloBatchItemResult { ProtocoloId = id, Numero = row.Numero, Impedido = message is not null, Mensagem = message ?? "Elegível" });
        }
        return new ProtocoloBatchPreview
        {
            Acao = command.Acao,
            AcaoLabel = ActionLabel(command.Acao),
            DestinoSetorId = command.DestinoSetorId,
            Destino = destinoNome,
            Responsavel = responsavel,
            Prazo = command.Prazo,
            Observacao = command.Observacao,
            Itens = itens
        };
    }

    public async Task<IReadOnlyList<ProtocoloBatchItemResult>> ExecuteBatchAsync(ProtocoloActor actor, ProtocoloLoteCommand command, CancellationToken ct)
    {
        var ids = (command.Ids ?? []).Where(x => x != Guid.Empty).Distinct().OrderBy(x => x).ToArray();
        var results = new List<ProtocoloBatchItemResult>();
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var id in ids)
            {
                var key = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : $"{command.IdempotencyKey}:{id:N}";
                var note = command.ObservacaoPorItem.TryGetValue(id, out var itemNote) ? itemNote : command.Observacao;
                var entregue = command.EntregueAPorItem.TryGetValue(id, out var itemEntregue) ? itemEntregue : null;
                ProtocoloCommandResult result = command.Acao switch
                {
                    "receber" => await ReceiveCoreAsync(conn, tx, actor, null, id, ProtocolCustodyRules.Waiting, false, ct),
                    "confirmar" => await ReceiveCoreAsync(conn, tx, actor, null, id, ProtocolCustodyRules.ReturnPending, true, ct),
                    "devolver" => await ReturnCoreWouldAsync(actor, conn, tx, id, note, key, ct),
                    _ => await ForwardCoreWouldAsync(actor, conn, tx, id, command.DestinoSetorId ?? Guid.Empty, command.Observacao, note, key, command.Prazo, command.ResponsavelId, entregue, ct)
                };
                results.Add(new ProtocoloBatchItemResult { ProtocoloId = id, Aplicado = result.Success, Impedido = !result.Success, Mensagem = result.Message });
            }
            if (results.Any(r => r.Aplicado)) await tx.CommitAsync(ct);
            else await tx.RollbackAsync(ct);
            return results;
        }
        catch (Exception)
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<ProtocoloCommandResult> CompleteMovementAsync(ProtocoloActor actor, Guid? movimentoId, Guid? protocoloId, string expected, bool isReturn, CancellationToken ct)
        => await MutateAsync(actor, (conn, tx) => ReceiveCoreAsync(conn, tx, actor, movimentoId, protocoloId, expected, isReturn, ct), ct);

    private async Task<ProtocoloCommandResult> ReceiveCoreAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, ProtocoloActor actor, Guid? movimentoId, Guid? protocoloId, string expected, bool isReturn, CancellationToken ct)
    {
        var grants = await LoadGrantsAsync(conn, tx, actor.TenantId, actor.UserId, ct);
        var move = await ResolveMovementAsync(conn, tx, actor, grants, movimentoId, protocoloId, expected, ct);
        if (move.Error is not null) return ProtocoloCommandResult.Fail(move.Error);
        var row = move.Row!;
        var verdict = ProtocolCustodyRules.DecideReceive(row.Situacao, row.RecebidaPor, actor.UserId);
        if (verdict == ReceiveVerdict.Idempotent) return ProtocoloCommandResult.Ok("Recebimento já confirmado por você.", row.Id, true);
        if (verdict == ReceiveVerdict.Conflict) return ProtocoloCommandResult.Fail("Esta movimentação já foi recebida por outro usuário.");
        if (verdict != ReceiveVerdict.Applied) return ProtocoloCommandResult.Fail("A movimentação não está pendente de recebimento.");
        if (!string.Equals(row.Situacao, expected, StringComparison.OrdinalIgnoreCase))
            return ProtocoloCommandResult.Fail("A movimentação identificada não corresponde à ação solicitada.");
        if (!ProtocolCustodyRules.CanAct(actor.CanSeeAll, grants.Any(g => g.SetorId == row.DestinoId && g.PodeReceber), true))
            return ProtocoloCommandResult.Fail("Você não tem direito de receber neste setor.");
        var nome = await UserNameAsync(conn, tx, actor.TenantId, actor.UserId, ct) ?? actor.UserName;
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo_tramitacao
set situacao_movimentacao=@Nova, ativa=false, recebida_em=now(), recebida_por=@UserId, recebida_por_nome=@Nome
where tenant_id=@TenantId and id=@Id and situacao_movimentacao=@Esperada;
""", new { Nova = isReturn ? ProtocolCustodyRules.ReturnConfirmed : ProtocolCustodyRules.Received, actor.UserId, Nome = nome, actor.TenantId, row.Id, Esperada = expected }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.protocolo
set setor_atual_id=@Destino, situacao_custodia='EM_CUSTODIA',
    recebido_por=@UserId, recebido_por_nome=@Nome, data_recebimento=now(),
    devolvido_por=case when @IsReturn then @UserId else devolvido_por end,
    devolvido_por_nome=case when @IsReturn then @Nome else devolvido_por_nome end,
    data_devolucao=case when @IsReturn then now() else data_devolucao end,
    updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@ProtocoloId;
""", new { row.DestinoId, actor.UserId, Nome = nome, IsReturn = isReturn, actor.TenantId, row.ProtocoloId }, tx, cancellationToken: ct));
        await UpsertParticipantAsync(conn, tx, actor.TenantId, row.ProtocoloId, row.DestinoId, true, ct);
        return ProtocoloCommandResult.Ok(isReturn ? "Retorno confirmado. Responsável, horário e histórico foram gravados juntos." : "Recebimento confirmado. A custódia passou ao setor de destino.", row.Id);
    }

    private Task<ProtocoloCommandResult> ForwardCoreWouldAsync(ProtocoloActor actor, System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid protocoloId, Guid destinoId, string? despacho, string? observacao, string? key, DateTime? prazo, Guid? responsavelId, string? entregueA, CancellationToken ct)
        => ForwardInsideAsync(actor, conn, tx, protocoloId, destinoId, despacho, observacao, key, prazo, responsavelId, entregueA, ct);

    private async Task<ProtocoloCommandResult> ForwardInsideAsync(ProtocoloActor actor, System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid protocoloId, Guid destinoId, string? despacho, string? observacao, string? key, DateTime? prazo, Guid? responsavelId, string? entregueA, CancellationToken ct)
    {
        if (destinoId == Guid.Empty) return ProtocoloCommandResult.Fail("Informe o setor de destino.");
        var destino = await SectorAsync(conn, tx, actor.TenantId, destinoId, ct);
        if (destino is null) return ProtocoloCommandResult.Fail("Setor de destino inválido.");
        var replay = await FindIdempotentAsync(conn, tx, actor.TenantId, key, ct);
        if (replay.HasValue) return ProtocoloCommandResult.Ok("Encaminhamento já registrado.", replay, true);
        var protocol = await LockProtocolAsync(conn, tx, actor.TenantId, protocoloId, ct);
        if (protocol is null) return ProtocoloCommandResult.Fail("Protocolo não encontrado.");
        if (ProtocolCustodyRules.IsClosed(protocol.Status)) return ProtocoloCommandResult.Fail("Processo encerrado.");
        var grants = await LoadGrantsAsync(conn, tx, actor.TenantId, actor.UserId, ct);
        var holder = protocol.SetorAtualId ?? protocol.SetorOrigemId;
        if (!ProtocolCustodyRules.CanAct(actor.CanSeeAll, grants.Any(g => g.SetorId == holder && g.PodeTramitar), true))
            return ProtocoloCommandResult.Fail("Sem direito de tramitar.");
        var active = await LockActiveAsync(conn, tx, actor.TenantId, protocoloId, null, ct);
        if (active is not null)
        {
            if (ProtocolCustodyRules.IsSameDestinationRepeat(active.Situacao, active.DestinoId, destinoId))
                return ProtocoloCommandResult.Ok("Encaminhamento já pendente.", active.Id, true);
            var block = ProtocolCustodyRules.SecondForwardBlock(active.Situacao, active.DestinoId, destinoId);
            if (block is not null) return ProtocoloCommandResult.Fail(block);
        }
        var id = await InsertMovementAsync(conn, tx, actor, protocol, null, destino.Id, destino.Nome, "TRAMITACAO", ProtocolCustodyRules.Waiting, despacho, observacao, null, key, prazo, responsavelId, entregueA, true, ct);
        await conn.ExecuteAsync(new CommandDefinition("update ged.protocolo set status=case when upper(status) in ('RASCUNHO','ABERTO') then 'EM_TRAMITACAO' else status end, situacao_custodia=@Situacao, updated_at=now(), updated_by=@UserId where tenant_id=@TenantId and id=@Id", new { Situacao = ProtocolCustodyRules.Waiting, actor.UserId, actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
        await UpsertParticipantAsync(conn, tx, actor.TenantId, protocoloId, destinoId, false, ct);
        return ProtocoloCommandResult.Ok("Encaminhado.", id);
    }

    private async Task<ProtocoloCommandResult> ReturnCoreWouldAsync(ProtocoloActor actor, System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid protocoloId, string? observacao, string? key, CancellationToken ct)
    {
        var replay = await FindIdempotentAsync(conn, tx, actor.TenantId, key, ct);
        if (replay.HasValue) return ProtocoloCommandResult.Ok("Devolução já registrada.", replay, true);
        var protocol = await LockProtocolAsync(conn, tx, actor.TenantId, protocoloId, ct);
        if (protocol is null) return ProtocoloCommandResult.Fail("Protocolo não encontrado.");
        if (ProtocolCustodyRules.IsClosed(protocol.Status)) return ProtocoloCommandResult.Fail("Processo encerrado.");
        var active = await LockActiveAsync(conn, tx, actor.TenantId, protocoloId, null, ct);
        if (active is not null) return ProtocoloCommandResult.Fail("Há movimentação pendente.");
        var grants = await LoadGrantsAsync(conn, tx, actor.TenantId, actor.UserId, ct);
        if (!ProtocolCustodyRules.CanAct(actor.CanSeeAll, grants.Any(g => g.SetorId == protocol.SetorAtualId && g.PodeTramitar), true))
            return ProtocoloCommandResult.Fail("Sem direito de devolver.");
        var back = await conn.QuerySingleOrDefaultAsync<(Guid? Id, string? Nome)>(new CommandDefinition("""
select setor_origem_id as Id, setor_origem_nome as Nome
from ged.protocolo_tramitacao
where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A' and situacao_movimentacao='RECEBIDA'
order by data_tramitacao desc
limit 1;
""", new { actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
        if (!back.Id.HasValue) back = (protocol.SetorOrigemId, null);
        var origem = back.Id.HasValue ? await SectorAsync(conn, tx, actor.TenantId, back.Id.Value, ct) : null;
        if (origem is null) return ProtocoloCommandResult.Fail("Setor de retorno inválido.");
        if (protocol.SetorAtualId == origem.Id) return ProtocoloCommandResult.Fail("A custódia já está no setor de retorno.");
        var id = await InsertMovementAsync(conn, tx, actor, protocol, null, origem.Id, origem.Nome, "DEVOLUCAO", ProtocolCustodyRules.ReturnPending, null, observacao, null, key, null, null, null, true, ct);
        await conn.ExecuteAsync(new CommandDefinition("update ged.protocolo set situacao_custodia=@Situacao, updated_at=now(), updated_by=@UserId where tenant_id=@TenantId and id=@Id", new { Situacao = ProtocolCustodyRules.ReturnPending, actor.UserId, actor.TenantId, Id = protocoloId }, tx, cancellationToken: ct));
        return ProtocoloCommandResult.Ok("Devolução pendente de confirmação.", id);
    }

    private async Task<Guid> InsertMovementAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, ProtocoloActor actor, ProtocolLock protocol, Guid? documentoId, Guid destinoId, string destinoNome, string acao, string situacao, string? despacho, string? observacao, string? justificativa, string? idempotencyKey, DateTime? prazo, Guid? responsavelId, string? entregueA, bool ativa, CancellationToken ct)
    {
        var origemNome = protocol.SetorAtualId.HasValue ? (await SectorAsync(conn, tx, actor.TenantId, protocol.SetorAtualId.Value, ct))?.Nome : null;
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
insert into ged.protocolo_tramitacao(
    tenant_id, protocolo_id, setor_origem_id, setor_origem_nome, setor_destino_id, setor_destino_nome,
    usuario_id, usuario_nome, acao, status_anterior, status_novo, despacho, observacao, justificativa,
    ip, user_agent, situacao_movimentacao, protocolo_documento_id, idempotency_key, correlation_id,
    ativa, prazo_em, entregue_a, responsavel_destino_id)
values (
    @TenantId, @ProtocoloId, @OrigemId, @OrigemNome, @DestinoId, @DestinoNome,
    @UserId, @UserName, @Acao, @StatusAnterior, @StatusNovo, @Despacho, @Obs, @Justificativa,
    @Ip, @Ua, @Situacao, @DocumentoId, @IdempotencyKey, @Correlation, @Ativa, @Prazo, @EntregueA, @Responsavel)
returning id;
""", new
        {
            actor.TenantId,
            ProtocoloId = protocol.Id,
            OrigemId = protocol.SetorAtualId,
            OrigemNome = origemNome,
            DestinoId = destinoId,
            DestinoNome = destinoNome,
            actor.UserId,
            UserName = actor.UserName,
            Acao = acao,
            StatusAnterior = protocol.Status,
            StatusNovo = protocol.Status,
            Despacho = despacho,
            Obs = observacao,
            Justificativa = justificativa,
            actor.Ip,
            Ua = actor.UserAgent,
            Situacao = situacao,
            DocumentoId = documentoId,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            Correlation = Guid.NewGuid().ToString("N"),
            Ativa = ativa,
            Prazo = prazo,
            EntregueA = entregueA,
            Responsavel = responsavelId
        }, tx, cancellationToken: ct));
    }

    private static async Task<(MoveRow? Row, string? Error)> ResolveMovementAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, ProtocoloActor actor, IReadOnlyList<Grant> grants, Guid? movimentoId, Guid? protocoloId, string expected, CancellationToken ct)
    {
        if (!movimentoId.HasValue || movimentoId == Guid.Empty)
        {
            if (!protocoloId.HasValue) return (null, "Identifique a movimentação a receber.");
            var ids = (await conn.QueryAsync<Guid>(new CommandDefinition("""
select id from ged.protocolo_tramitacao
where tenant_id=@TenantId and protocolo_id=@ProtocoloId and reg_status='A' and ativa=true
  and situacao_movimentacao=@Expected
  and (@Admin or setor_destino_id = any(@Setores))
order by data_tramitacao;
""", new { actor.TenantId, ProtocoloId = protocoloId, Expected = expected, Admin = actor.CanSeeAll, Setores = grants.Select(g => g.SetorId).ToArray() }, tx, cancellationToken: ct))).ToList();
            if (ids.Count == 0) return (null, "Não há movimentação pendente identificada para o seu setor.");
            if (ids.Count > 1) return (null, "Há mais de uma movimentação pendente. Selecione qual transferência deseja receber.");
            movimentoId = ids[0];
        }
        var row = await conn.QuerySingleOrDefaultAsync<MoveRow>(new CommandDefinition("""
select id as Id, protocolo_id as ProtocoloId, situacao_movimentacao as Situacao, setor_destino_id as DestinoId, recebida_por as RecebidaPor
from ged.protocolo_tramitacao
where tenant_id=@TenantId and id=@Id and reg_status='A'
for update;
""", new { actor.TenantId, Id = movimentoId }, tx, cancellationToken: ct));
        if (row is null) return (null, "Movimentação não encontrada neste tenant.");
        if (protocoloId.HasValue && row.ProtocoloId != protocoloId.Value) return (null, "A movimentação não pertence ao protocolo informado.");
        return (row, null);
    }

    private async Task<ProtocoloCentralDetail?> LoadDetailAsync(System.Data.Common.DbConnection conn, Guid tenantId, Guid id, IReadOnlyList<Grant> grants, bool canSeeAll, Guid[] visibleDocuments, CancellationToken ct)
    {
        var visibility = new ProtocoloCentralQuery { TenantId = tenantId, CanSeeAll = canSeeAll, Visao = "historico" };
        var sectors = grants.Select(g => g.SetorId).ToArray();
        var parameters = BuildParameters(visibility, sectors, visibleDocuments);
        parameters.Add("Id", id);
        if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"select exists(select 1 from ged.protocolo p where p.id=@Id and {BuildWhere(visibility, sectors)})", parameters, cancellationToken: ct))) return null;
        var detail = await conn.QuerySingleOrDefaultAsync<ProtocoloCentralDetail>(new CommandDefinition("""
select p.id as Id, p.numero as Numero, p.assunto as Assunto, p.descricao as Descricao, p.interessado as Interessado,
       p.tipo_solicitacao as Tipo, p.status as Status, p.situacao_custodia as SituacaoCustodia,
       so.nome as SetorOrigem, sa.nome as SetorAtual, m.setor_destino_nome as SetorDestino,
       m.usuario_nome as Remetente, coalesce(m.recebida_por_nome, u.name) as Destinatario,
       m.data_tramitacao as EnviadoEm, m.recebida_em as RecebidoEm, m.id as MovimentoId, m.situacao_movimentacao as SituacaoMovimentacao,
       p.setor_atual_id as SetorAtualId
from ged.protocolo p
left join ged.protocolo_setor so on so.id=p.setor_origem_id
left join ged.protocolo_setor sa on sa.id=p.setor_atual_id
left join lateral (
    select * from ged.protocolo_tramitacao t
    where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A' and t.situacao_movimentacao is not null
    order by t.ativa desc, t.data_tramitacao desc limit 1
) m on true
left join ged.app_user u on u.tenant_id=p.tenant_id and u.id=m.responsavel_destino_id
where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A';
""", new { TenantId = tenantId, Id = id }, cancellationToken: ct));
        if (detail is null) return null;
        detail.ExibirCamposHospitalares = IsHospital(detail.Tipo) || !string.IsNullOrWhiteSpace(detail.Interessado);
        detail.Eventos = (await conn.QueryAsync<ProtocoloCustodiaEvento>(new CommandDefinition("""
select id as Id, acao as Acao, situacao_movimentacao as Situacao, setor_origem_nome as Origem, setor_destino_nome as Destino,
       usuario_nome as Remetente, recebida_por_nome as Recebedor, data_tramitacao as Quando, recebida_em as RecebidaEm,
       despacho as Despacho, justificativa as Justificativa, ativa as Ativa
from ged.protocolo_tramitacao
where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A'
order by data_tramitacao desc;
""", new { TenantId = tenantId, Id = id }, cancellationToken: ct))).ToList();
        detail.CustodiaFisica = await LoadPhysicalAsync(conn, tenantId, id, visibleDocuments, ct);
        var flags = new ProtocoloCentralRow
        {
            Status = detail.Status,
            SituacaoCustodia = detail.SituacaoCustodia,
            SituacaoMovimentacao = detail.SituacaoMovimentacao,
            SetorAtualId = detail.SetorAtualId,
            MovimentoDestinoId = detail.Eventos.FirstOrDefault(e => e.Ativa)?.Id == null ? null : null
        };
        var active = detail.Eventos.FirstOrDefault(e => e.Ativa);
        if (active is not null)
        {
            var ids = await conn.QuerySingleOrDefaultAsync<(Guid? Origem, Guid? Destino)>(new CommandDefinition("select setor_origem_id, setor_destino_id from ged.protocolo_tramitacao where tenant_id=@TenantId and id=@Id", new { TenantId = tenantId, active.Id }, cancellationToken: ct));
            flags.MovimentoOrigemId = ids.Origem;
            flags.MovimentoDestinoId = ids.Destino;
            flags.MovimentoId = active.Id;
        }
        flags.SetorAtualId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("select setor_atual_id from ged.protocolo where tenant_id=@TenantId and id=@Id", new { TenantId = tenantId, Id = id }, cancellationToken: ct));
        ApplyFlags(flags, grants, canSeeAll);
        detail.PodeReceber = flags.PodeReceber;
        detail.PodeTramitar = flags.PodeTramitar;
        detail.PodeDevolver = flags.PodeDevolver;
        detail.PodeEstornar = flags.PodeEstornar;
        detail.PodeConfirmarRetorno = flags.PodeConfirmarRetorno;
        return detail;
    }

    private static async Task<List<ProtocoloCustodiaFisica>> LoadPhysicalAsync(System.Data.Common.DbConnection conn, Guid tenantId, Guid protocoloId, Guid[] visibleDocuments, CancellationToken ct)
    {
        var list = new List<ProtocoloCustodiaFisica>();
        try
        {
        if (await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.physical_loan') is not null", cancellationToken: ct)))
        {
            var rows = await conn.QueryAsync<ProtocoloCustodiaFisica>(new CommandDefinition("""
select 'Empréstimo físico' as Origem, coalesce(d.title, d.code) as Documento, l.sector_name as Localizacao,
       coalesce(l.carrier_name, l.sector_name) as Detentor, l.due_at as Previsao, l.loaned_at as EntregueEm, l.returned_at as RetornoEm, l.status as Situacao
from ged.physical_loan l
join ged.protocolo_documento_ged g on g.tenant_id=l.tenant_id and g.ged_document_id=l.document_id and g.reg_status='A'
left join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id
where l.tenant_id=@TenantId and g.protocolo_id=@Id and l.reg_status='A' and d.id=any(@VisibleDocuments)
order by l.loaned_at desc nulls last;
""", new { TenantId = tenantId, Id = protocoloId, VisibleDocuments = visibleDocuments }, cancellationToken: ct));
            list.AddRange(rows);
        }
        if (await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.loan_request') is not null", cancellationToken: ct)))
        {
            var rows = await conn.QueryAsync<ProtocoloCustodiaFisica>(new CommandDefinition("""
select 'Solicitação de empréstimo' as Origem, coalesce(d.title, d.code) as Documento, lr.current_sector_name as Localizacao,
       lr.requester_name as Detentor, lr.due_at as Previsao, lr.delivered_at as EntregueEm, lr.returned_at as RetornoEm, lr.status::text as Situacao
from ged.loan_request lr
join ged.protocolo_documento_ged g on g.tenant_id=lr.tenant_id and g.ged_document_id=lr.document_id and g.reg_status='A'
left join ged.document d on d.tenant_id=lr.tenant_id and d.id=lr.document_id
where lr.tenant_id=@TenantId and g.protocolo_id=@Id and lr.reg_status='A' and d.id=any(@VisibleDocuments);
""", new { TenantId = tenantId, Id = protocoloId, VisibleDocuments = visibleDocuments }, cancellationToken: ct));
            list.AddRange(rows);
        }
        }
        catch (PostgresException)
        {
            // A fila principal continua disponível quando o módulo físico ainda não tem todas as colunas.
        }
        return list;
    }

    private static void ApplyFlags(ProtocoloCentralRow row, IReadOnlyList<Grant> grants, bool canSeeAll)
    {
        var closed = ProtocolCustodyRules.IsClosed(row.Status);
        var pending = ProtocolCustodyRules.IsPending(row.SituacaoMovimentacao);
        bool linked(Guid? setor, Func<Grant, bool> right) => setor.HasValue && grants.Any(g => g.SetorId == setor && right(g));
        row.PodeReceber = !closed && string.Equals(row.SituacaoMovimentacao, ProtocolCustodyRules.Waiting, StringComparison.OrdinalIgnoreCase)
            && ProtocolCustodyRules.CanAct(canSeeAll, linked(row.MovimentoDestinoId, g => g.PodeReceber), true);
        row.PodeConfirmarRetorno = !closed && string.Equals(row.SituacaoMovimentacao, ProtocolCustodyRules.ReturnPending, StringComparison.OrdinalIgnoreCase)
            && ProtocolCustodyRules.CanAct(canSeeAll, linked(row.MovimentoDestinoId, g => g.PodeReceber), true);
        row.PodeTramitar = !closed && !pending && ProtocolCustodyRules.CanAct(canSeeAll, linked(row.SetorAtualId, g => g.PodeTramitar), true);
        row.PodeDevolver = !closed && !pending && ProtocolCustodyRules.CanAct(canSeeAll, linked(row.SetorAtualId, g => g.PodeTramitar), true);
        row.PodeEstornar = !closed && pending && ProtocolCustodyRules.CanAct(canSeeAll, linked(row.MovimentoOrigemId, g => g.PodeTramitar), true);
    }

    private static async Task<ProtocoloCentralCounts> CountAsync(System.Data.Common.DbConnection conn, ProtocoloCentralQuery query, Guid[] setores, Guid[] visibleDocuments, CancellationToken ct)
    {
        var p = BuildParameters(query, setores, visibleDocuments);
        async Task<int> Count(string visao)
        {
            var q = new ProtocoloCentralQuery
            {
                TenantId = query.TenantId, CanSeeAll = query.CanSeeAll, Visao = visao, Q = query.Q, Status = query.Status,
                Tipo = query.Tipo, SetorId = query.SetorId, ResponsavelId = query.ResponsavelId, De = query.De, Ate = query.Ate, PrazoVencido = query.PrazoVencido
            };
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition($"select count(*)::int from ged.protocolo p where {BuildWhere(q, setores)}", BuildParameters(q, setores, visibleDocuments), cancellationToken: ct));
        }
        return new ProtocoloCentralCounts
        {
            AReceber = await Count("a_receber"),
            ATramitar = await Count("a_tramitar"),
            Enviados = await Count("enviados"),
            Devolucoes = await Count("devolucoes")
        };
    }

    private static DynamicParameters BuildParameters(ProtocoloCentralQuery query, Guid[] setores, Guid[] visibleDocuments)
    {
        var p = new DynamicParameters();
        p.Add("VisibleDocuments", visibleDocuments);
        p.Add("TenantId", query.TenantId);
        p.Add("Setores", setores);
        p.Add("CanSeeAll", query.CanSeeAll);
        if (!string.IsNullOrWhiteSpace(query.Q)) p.Add("Q", $"%{query.Q.Trim()}%");
        if (!string.IsNullOrWhiteSpace(query.Status)) p.Add("Status", query.Status.Trim());
        if (!string.IsNullOrWhiteSpace(query.Tipo)) p.Add("Tipo", $"%{query.Tipo.Trim()}%");
        if (query.SetorId.HasValue) p.Add("SetorId", query.SetorId);
        if (query.ResponsavelId.HasValue) p.Add("ResponsavelId", query.ResponsavelId);
        if (query.De.HasValue) p.Add("De", query.De.Value.Date);
        if (query.Ate.HasValue) p.Add("Ate", query.Ate.Value.Date.AddDays(1));
        return p;
    }

    private static string BuildWhere(ProtocoloCentralQuery query, Guid[] setores)
    {
        var sql = new StringBuilder("p.tenant_id=@TenantId and p.reg_status='A'");
        if (!query.CanSeeAll)
        {
            sql.Append("""
 and (
    p.setor_atual_id = any(@Setores) or p.setor_origem_id = any(@Setores)
    or exists(select 1 from ged.protocolo_setor_participante sp where sp.tenant_id=p.tenant_id and sp.protocolo_id=p.id and sp.setor_id=any(@Setores) and sp.pode_visualizar=true)
    or exists(select 1 from ged.protocolo_tramitacao t where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A' and (t.setor_destino_id=any(@Setores) or t.setor_origem_id=any(@Setores)))
 )
""");
        }
        sql.Append(query.Visao switch
        {
            "a_receber" => """
 and exists(select 1 from ged.protocolo_tramitacao t where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A' and t.ativa=true and t.situacao_movimentacao='AGUARDANDO_RECEBIMENTO' and (@CanSeeAll or t.setor_destino_id=any(@Setores)))
""",
            "a_tramitar" => $"""
 and upper(p.status) not in {ClosedSql}
 and coalesce(p.situacao_custodia,'EM_CUSTODIA') in ('EM_CUSTODIA','RECEBIDA','RETORNO_CONFIRMADO')
 and not exists(select 1 from ged.protocolo_tramitacao t where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A' and t.ativa=true)
 and (@CanSeeAll or p.setor_atual_id=any(@Setores))
""",
            "enviados" => """
 and exists(select 1 from ged.protocolo_tramitacao t where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A' and t.ativa=true and t.situacao_movimentacao in ('AGUARDANDO_RECEBIMENTO','DEVOLUCAO_PENDENTE') and (@CanSeeAll or t.setor_origem_id=any(@Setores)))
""",
            "devolucoes" => """
 and exists(select 1 from ged.protocolo_tramitacao t where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A' and t.ativa=true and t.situacao_movimentacao='DEVOLUCAO_PENDENTE' and (@CanSeeAll or t.setor_destino_id=any(@Setores) or t.setor_origem_id=any(@Setores)))
""",
            _ => ""
        });
        if (!string.IsNullOrWhiteSpace(query.Q))
            sql.Append(" and (p.numero ilike @Q or p.assunto ilike @Q or coalesce(p.interessado,'') ilike @Q or exists(select 1 from ged.protocolo_documento_ged g join ged.document d on d.tenant_id=g.tenant_id and d.id=g.ged_document_id where g.tenant_id=p.tenant_id and g.protocolo_id=p.id and g.reg_status='A' and d.id=any(@VisibleDocuments) and (coalesce(d.code,'') ilike @Q or coalesce(d.title,'') ilike @Q)))");
        if (!string.IsNullOrWhiteSpace(query.Status)) sql.Append(" and upper(p.status)=upper(@Status)");
        if (!string.IsNullOrWhiteSpace(query.Tipo)) sql.Append(" and coalesce(p.tipo_solicitacao,'') ilike @Tipo");
        if (query.SetorId.HasValue) sql.Append(" and (p.setor_atual_id=@SetorId or exists(select 1 from ged.protocolo_tramitacao t where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A' and t.ativa=true and (t.setor_destino_id=@SetorId or t.setor_origem_id=@SetorId)))");
        if (query.ResponsavelId.HasValue) sql.Append(" and (p.recebido_por=@ResponsavelId or exists(select 1 from ged.protocolo_tramitacao t where t.tenant_id=p.tenant_id and t.protocolo_id=p.id and t.reg_status='A' and (t.usuario_id=@ResponsavelId or t.recebida_por=@ResponsavelId or t.responsavel_destino_id=@ResponsavelId)))");
        if (query.De.HasValue) sql.Append(" and p.created_at >= @De");
        if (query.Ate.HasValue) sql.Append(" and p.created_at < @Ate");
        if (query.PrazoVencido) sql.Append($" and p.data_prazo is not null and p.data_prazo < now() and upper(p.status) not in {ClosedSql}");
        _ = setores;
        return sql.ToString();
    }

    private static async Task<List<Grant>> LoadGrantsAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction? tx, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<Grant>(new CommandDefinition("""
select us.setor_id as SetorId, s.nome as Nome, us.pode_receber as PodeReceber, us.pode_tramitar as PodeTramitar, us.pode_decidir as PodeDecidir
from ged.protocolo_usuario_setor us
join ged.protocolo_setor s on s.tenant_id=us.tenant_id and s.id=us.setor_id
where us.tenant_id=@TenantId and us.usuario_id=@UserId and us.reg_status='A' and us.ativo=true
  and s.reg_status='A' and s.ativo=true;
""", new { TenantId = tenantId, UserId = userId }, tx, cancellationToken: ct));
        return rows.ToList();
    }

    private static async Task<ProtocolLock?> LockProtocolAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, Guid id, CancellationToken ct)
        => await conn.QuerySingleOrDefaultAsync<ProtocolLock>(new CommandDefinition("""
select id as Id, status as Status, setor_atual_id as SetorAtualId, setor_origem_id as SetorOrigemId, numero as Numero
from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A' for update;
""", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));

    private static async Task<ActiveMove?> LockActiveAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, Guid protocoloId, Guid? documentoId, CancellationToken ct)
        => await conn.QuerySingleOrDefaultAsync<ActiveMove>(new CommandDefinition("""
select id as Id, situacao_movimentacao as Situacao, setor_destino_id as DestinoId, setor_origem_id as OrigemId
from ged.protocolo_tramitacao
where tenant_id=@TenantId and protocolo_id=@ProtocoloId and reg_status='A' and ativa=true
  and coalesce(protocolo_documento_id, '00000000-0000-0000-0000-000000000000'::uuid)=coalesce(@DocumentoId, '00000000-0000-0000-0000-000000000000'::uuid)
for update;
""", new { TenantId = tenantId, ProtocoloId = protocoloId, DocumentoId = documentoId }, tx, cancellationToken: ct));

    private static async Task<ProtocoloSetorOption?> SectorAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction? tx, Guid tenantId, Guid id, CancellationToken ct)
        => await conn.QuerySingleOrDefaultAsync<ProtocoloSetorOption>(new CommandDefinition("""
select id as Id, nome as Nome from ged.protocolo_setor
where tenant_id=@TenantId and id=@Id and reg_status='A' and ativo=true;
""", new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));

    private static async Task<bool> UserInSectorAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, Guid userId, Guid setorId, CancellationToken ct)
        => await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
    select 1 from ged.protocolo_usuario_setor us
    join ged.app_user u on u.tenant_id=us.tenant_id and u.id=us.usuario_id
    where us.tenant_id=@TenantId and us.usuario_id=@UserId and us.setor_id=@SetorId
      and us.reg_status='A' and us.ativo=true and u.is_active=true and u.deleted_at_utc is null);
""", new { TenantId = tenantId, UserId = userId, SetorId = setorId }, tx, cancellationToken: ct));

    private static async Task<string?> UserNameAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, Guid userId, CancellationToken ct)
        => await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select coalesce(name, email) from ged.app_user where tenant_id=@TenantId and id=@UserId", new { TenantId = tenantId, UserId = userId }, tx, cancellationToken: ct));

    private static async Task<Guid?> FindIdempotentAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("select id from ged.protocolo_tramitacao where tenant_id=@TenantId and idempotency_key=@Key and reg_status='A'", new { TenantId = tenantId, Key = key.Trim() }, tx, cancellationToken: ct));
    }

    private static async Task UpsertParticipantAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, Guid tenantId, Guid protocoloId, Guid setorId, bool editar, CancellationToken ct)
        => await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocolo_setor_participante(tenant_id, protocolo_id, setor_id, pode_visualizar, pode_editar)
values (@TenantId, @ProtocoloId, @SetorId, true, @Editar)
on conflict (tenant_id, protocolo_id, setor_id)
do update set pode_visualizar=true, pode_editar=ged.protocolo_setor_participante.pode_editar or excluded.pode_editar, participou_em=now();
""", new { TenantId = tenantId, ProtocoloId = protocoloId, SetorId = setorId, Editar = editar }, tx, cancellationToken: ct));

    private async Task<ProtocoloCommandResult> MutateAsync(ProtocoloActor actor, Func<System.Data.Common.DbConnection, System.Data.Common.DbTransaction, Task<ProtocoloCommandResult>> action, CancellationToken ct)
    {
        if (actor.UserId == Guid.Empty) return ProtocoloCommandResult.Fail("Usuário inválido.");
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var result = await action(conn, tx);
            if (result.Success) await tx.CommitAsync(ct);
            else await tx.RollbackAsync(ct);
            return result;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            return ProtocoloCommandResult.Fail("Já existe uma movimentação ativa incompatível ou este comando já foi processado.");
        }
    }

    private static string? PreviewMessage(ProtocoloLoteCommand command, string? status, string? situacao, Guid? destino)
    {
        if (ProtocolCustodyRules.IsClosed(status)) return "Processo encerrado. Reabra antes de operar.";
        return command.Acao switch
        {
            "receber" when !string.Equals(situacao, ProtocolCustodyRules.Waiting, StringComparison.OrdinalIgnoreCase) => "Não há recebimento pendente.",
            "confirmar" when !string.Equals(situacao, ProtocolCustodyRules.ReturnPending, StringComparison.OrdinalIgnoreCase) => "Não há devolução pendente de confirmação.",
            "devolver" when ProtocolCustodyRules.IsPending(situacao) => "Já existe movimentação pendente.",
            "tramitar" when ProtocolCustodyRules.IsPending(situacao) && destino != command.DestinoSetorId => "Já existe encaminhamento ativo para outro destino.",
            "tramitar" when !command.DestinoSetorId.HasValue => "Informe o destino do lote.",
            _ => null
        };
    }

    private static bool RequiresDestination(string acao) => string.Equals(acao, "tramitar", StringComparison.OrdinalIgnoreCase);
    private static string ActionLabel(string acao) => acao switch
    {
        "receber" => "Receber",
        "devolver" => "Devolver",
        "confirmar" => "Confirmar retorno",
        _ => "Tramitar"
    };
    private static string NormalizeVisao(string? visao) => (visao ?? "").Trim().ToLowerInvariant() switch
    {
        "entrada" => "a_tramitar",
        "enviados" => "enviados",
        "historico" => "historico",
        "geral" => "geral",
        "devolucoes" => "devolucoes",
        "a_tramitar" => "a_tramitar",
        _ => "a_receber"
    };
    private static bool IsHospital(string? tipo)
    {
        var value = (tipo ?? "").ToUpperInvariant();
        return value.Contains("PRONT") || value.Contains("PACIENT") || value.Contains("HOSPITAL") || value.Contains("CLINIC");
    }

    private static async Task WriteDraftHistoryAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        ProtocoloActor actor,
        Guid protocoloId,
        ProtocolDraftForDispatch draft,
        string status,
        object details,
        CancellationToken ct)
    {
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocolo_minuta_historico (
    id, tenant_id, protocolo_id, minuta_id, versao, evento, status, titulo,
    conteudo, usuario_id, usuario_nome, detalhes, created_at
) values (
    @Id, @TenantId, @ProtocoloId, @MinutaId, @Versao, @Evento, @Status, @Titulo,
    @Conteudo, @UserId, @UserName, @Details::jsonb, now()
);
""", new
        {
            Id = Guid.NewGuid(),
            actor.TenantId,
            ProtocoloId = protocoloId,
            MinutaId = draft.Id,
            draft.Versao,
            Evento = status,
            Status = status,
            draft.Titulo,
            draft.Conteudo,
            actor.UserId,
            actor.UserName,
            Details = JsonSerializer.Serialize(details)
        }, tx, cancellationToken: ct));
        if (rows != 1)
            throw new InvalidOperationException("Falha ao registrar o histórico da minuta na tramitação.");
    }

    private sealed class ProtocolDraftForDispatch
    {
        public Guid Id { get; set; }
        public Guid ProtocoloId { get; set; }
        public Guid SetorId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Conteudo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Versao { get; set; }
    }

    private sealed class Grant
    {
        public Guid SetorId { get; set; }
        public string Nome { get; set; } = "";
        public bool PodeReceber { get; set; }
        public bool PodeTramitar { get; set; }
        public bool PodeDecidir { get; set; }
    }

    private sealed class ProtocolLock
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public Guid? SetorAtualId { get; set; }
        public Guid? SetorOrigemId { get; set; }
        public string Numero { get; set; } = "";
    }

    private sealed class ActiveMove
    {
        public Guid Id { get; set; }
        public string? Situacao { get; set; }
        public Guid? DestinoId { get; set; }
        public Guid? OrigemId { get; set; }
        public string? StatusAnterior { get; set; }
        public bool Ativa { get; set; }
    }

    private sealed class MoveRow
    {
        public Guid Id { get; set; }
        public Guid ProtocoloId { get; set; }
        public string? Situacao { get; set; }
        public Guid DestinoId { get; set; }
        public Guid? RecebidaPor { get; set; }
    }
}
