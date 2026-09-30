using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.RetentionCases;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.RetentionCases;

public sealed class RetentionCaseExecutionRepository : IRetentionCaseExecutionRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<RetentionCaseExecutionRepository> _logger;

    public RetentionCaseExecutionRepository(IDbConnectionFactory db, ILogger<RetentionCaseExecutionRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ExecuteCaseResult> ExecuteCaseAsync(Guid tenantId, Guid userId, Guid caseId, CancellationToken ct)
    {
        // ✅ Regras de bloqueio:
        // - HOLD no documento bloqueia
        // - se classificação exigir assinatura e não tiver como checar aqui, bloqueia por critério conservador
        // - se classificação for sigilosa, bloqueia (pode virar "permitir com permissão" depois)

        const string sqlFetch = @"
select
  i.id as ItemId,
  i.document_id as DocumentId,
  coalesce(i.suggested_destination,'REAVALIAR') as Dest,
  d.retention_hold as Hold,
  c.requires_digital_signature as ReqSign,
  c.is_confidential as Confidential
from ged.retention_case_item i
join ged.document d
  on d.tenant_id=i.tenant_id and d.id=i.document_id
left join ged.classification_plan c
  on c.tenant_id=i.tenant_id and c.id=i.classification_id
where i.tenant_id=@tenantId
  and i.case_id=@caseId
  and i.decision='APPROVE'
  and i.executed_at is null
order by i.id;
";

        const string sqlMarkBlocked = @"
update ged.retention_case_item
set
  execution_status = 'BLOCKED',
  execution_block_reason = @reason,
  execution_block_source = @source,
  decision_notes = coalesce(decision_notes,'') || @reason
where tenant_id=@tenantId and id=@itemId and decision='APPROVE';
";

        const string sqlExecItem = @"
update ged.retention_case_item
set executed_at = now(),
    executed_by = @userId,
    execution_status = 'EXECUTED',
    execution_block_reason = null,
    execution_block_source = null
where tenant_id=@tenantId and id=@itemId and decision='APPROVE' and executed_at is null;
";

        const string sqlUpdateDoc = @"
update ged.document
set
  disposition_status = @dispStatus,
  disposition_case_id = @caseId,
  disposition_at = now(),
  disposition_by = @userId
where tenant_id=@tenantId and id=@documentId;
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var caseStatus = await conn.ExecuteScalarAsync<string?>(
                "select status from ged.retention_case where tenant_id=@tenantId and id=@caseId for update",
                new { tenantId, caseId }, tx);
            if (caseStatus is not ("APPROVED" or "PARTIALLY_EXECUTED"))
                throw new InvalidOperationException("O caso não está aprovado para execução.");

            var rows = (await conn.QueryAsync(sqlFetch, new { tenantId, caseId }, tx)).ToList();
            var hasPhysical = await conn.ExecuteScalarAsync<bool>("select to_regclass('ged.physical_loan') is not null", transaction: tx);
            var hasLoanRequest = await conn.ExecuteScalarAsync<bool>("select to_regclass('ged.loan_request') is not null", transaction: tx);
            var hasProtocolMove = await conn.ExecuteScalarAsync<bool>("select to_regclass('ged.protocolo_tramitacao') is not null", transaction: tx);
            var hasRequestMove = await conn.ExecuteScalarAsync<bool>("""
select exists(
  select 1 from information_schema.columns
  where table_schema='ged' and table_name='protocol_tramitation' and column_name='item_id')
""", transaction: tx);

            int executed = 0, blocked = 0;

            foreach (var r in rows)
            {
                long itemId = r.itemid;
                Guid docId = r.documentid;
                string dest = (string)r.dest;
                bool hold = r.hold ?? false;
                bool reqSign = r.reqsign ?? false;
                bool confidential = r.confidential ?? false;

                string? source = null;
                string? reason = null;
                if (hold)
                {
                    source = "HOLD";
                    reason = "\n[BLOQUEIO HOLD] Documento em retenção operacional. Regularize o HOLD antes de executar a destinação.";
                }
                else if (confidential)
                {
                    source = "SIGILO";
                    reason = "\n[BLOQUEIO SIGILO] Documento sigiloso. A proteção permanece até validação autorizada.";
                }
                else if (reqSign)
                {
                    source = "ASSINATURA";
                    reason = "\n[BLOQUEIO ASSINATURA] A classe exige assinatura digital. Conclua a assinatura antes da destinação.";
                }
                else if (hasPhysical && await conn.ExecuteScalarAsync<bool>("""
select exists(
  select 1 from ged.physical_loan l
  where l.tenant_id=@tenantId and l.document_id=@docId and l.reg_status='A'
    and l.returned_at is null
    and upper(coalesce(l.status,'')) not in ('RETURNED','CANCELLED','DEVOLVIDO','CANCELADO'))
""", new { tenantId, docId }, tx))
                {
                    source = "EMPRESTIMO_FISICO";
                    reason = "\n[BLOQUEIO EMPRÉSTIMO FÍSICO] Há custódia física em aberto para este documento. Confirme o retorno antes de reavaliar a destinação.";
                }
                else if (hasLoanRequest && await conn.ExecuteScalarAsync<bool>("""
select exists(
  select 1 from ged.loan_request lr
  where lr.tenant_id=@tenantId and lr.document_id=@docId and lr.reg_status='A'
    and lr.status in ('REQUESTED','APPROVED','DELIVERED','OVERDUE'))
""", new { tenantId, docId }, tx))
                {
                    source = "EMPRESTIMO_SOLICITACAO";
                    reason = "\n[BLOQUEIO EMPRÉSTIMO] Há solicitação de empréstimo em aberto vinculada a este documento. Conclua ou cancele o empréstimo.";
                }
                else if (hasProtocolMove && await conn.ExecuteScalarAsync<bool>("""
select exists(
  select 1
  from ged.protocolo_documento_ged g
  join ged.protocolo_tramitacao t on t.tenant_id=g.tenant_id and t.protocolo_id=g.protocolo_id and t.reg_status='A' and t.ativa=true
    and t.situacao_movimentacao in ('AGUARDANDO_RECEBIMENTO','DEVOLUCAO_PENDENTE')
  where g.tenant_id=@tenantId and g.ged_document_id=@docId and g.reg_status='A'
    and (t.protocolo_documento_id is null or t.protocolo_documento_id=g.protocolo_documento_id))
""", new { tenantId, docId }, tx))
                {
                    source = "MOVIMENTACAO_PROTOCOLO";
                    reason = "\n[BLOQUEIO MOVIMENTAÇÃO] Este documento está em tramitação física/institucional pendente. Receba ou confirme o retorno antes da destinação.";
                }
                else if (hasRequestMove && await conn.ExecuteScalarAsync<bool>("""
select exists(
  select 1
  from ged.protocol_request_item i
  join ged.protocol_tramitation t on t.tenant_id=i.tenant_id and t.protocol_request_id=i.protocol_request_id and t.reg_status='A'
    and t.status in ('PENDING_RECEIPT','RETURN_PENDING')
    and (t.item_id is null or t.item_id=i.id)
  where i.tenant_id=@tenantId and i.document_id=@docId and i.reg_status='A')
""", new { tenantId, docId }, tx))
                {
                    source = "MOVIMENTACAO_SOLICITACAO";
                    reason = "\n[BLOQUEIO SOLICITAÇÃO] O item documental está em movimentação pendente da solicitação. A decisão arquivística permanece; regularize a custódia.";
                }

                if (source is not null)
                {
                    blocked++;
                    await conn.ExecuteAsync(sqlMarkBlocked, new { tenantId, itemId, reason, source }, tx);
                    continue;
                }

                // Mapear destino -> disposition_status
                string dispStatus = dest switch
                {
                    "ELIMINAR" => "ELIMINATION_READY",
                    "TRANSFERIR" => "TRANSFER_READY",
                    "RECOLHER" => "TRANSFER_READY",
                    _ => "REVIEW_REQUIRED"
                };

                await conn.ExecuteAsync(sqlUpdateDoc, new { tenantId, documentId = docId, dispStatus, caseId, userId }, tx);
                await conn.ExecuteAsync(sqlExecItem, new { tenantId, itemId, userId }, tx);
                executed++;
            }

            var outcome = ProtocolCustodyRules.RetentionOutcome(executed, blocked);
            var nextStatus = ProtocolCustodyRules.RetentionCaseStatus(outcome);
            if (nextStatus is null)
            {
                await conn.ExecuteAsync(@"
update ged.retention_case
set execution_outcome=@outcome
where tenant_id=@tenantId and id=@caseId;", new { tenantId, caseId, outcome }, tx);
            }
            else
            {
                await conn.ExecuteAsync(@"
update ged.retention_case
set status=@status,
    execution_outcome=@outcome,
    closed_at = case when @status='EXECUTED' then coalesce(closed_at, now()) else closed_at end,
    closed_by = case when @status='EXECUTED' then coalesce(closed_by, @userId) else closed_by end
where tenant_id=@tenantId and id=@caseId;", new { tenantId, caseId, userId, status = nextStatus, outcome }, tx);
            }

            await tx.CommitAsync(ct);

            return new ExecuteCaseResult(executed, blocked, outcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteCaseAsync failed. Tenant={TenantId} Case={CaseId}", tenantId, caseId);
            throw;
        }
    }
}