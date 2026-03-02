using Dapper;
using InovaGed.Application.Common.Database;
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
        // - se classificação exigir assinatura e não tiver como checar aqui, bloqueia (exemplo conservador)
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
  decision = 'REJECT',
  decision_notes = coalesce(decision_notes,'') || @reason,
  decided_at = coalesce(decided_at, now()),
  decided_by = coalesce(decided_by, @userId)
where tenant_id=@tenantId and id=@itemId;
";

        const string sqlExecItem = @"
update ged.retention_case_item
set executed_at = now(),
    executed_by = @userId
where tenant_id=@tenantId and id=@itemId;
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

            var rows = (await conn.QueryAsync(sqlFetch, new { tenantId, caseId }, tx)).ToList();

            int executed = 0, blocked = 0;

            foreach (var r in rows)
            {
                long itemId = r.itemid;
                Guid docId = r.documentid;
                string dest = (string)r.dest;
                bool hold = r.hold ?? false;
                bool reqSign = r.reqsign ?? false;
                bool confidential = r.confidential ?? false;

                // Bloqueio conservador (pode relaxar por permissão depois)
                if (hold)
                {
                    blocked++;
                    await conn.ExecuteAsync(sqlMarkBlocked, new { tenantId, itemId, userId, reason = "\n[BLOCK] Documento em HOLD." }, tx);
                    continue;
                }

                if (confidential)
                {
                    blocked++;
                    await conn.ExecuteAsync(sqlMarkBlocked, new { tenantId, itemId, userId, reason = "\n[BLOCK] Documento sigiloso (requer fluxo/perm.)." }, tx);
                    continue;
                }

                if (reqSign)
                {
                    blocked++;
                    await conn.ExecuteAsync(sqlMarkBlocked, new { tenantId, itemId, userId, reason = "\n[BLOCK] Classe exige assinatura digital (validar pendências)." }, tx);
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

            // Se executou ao menos um item, marca o caso como EXECUTED (ou mantém APPROVED e só registra execução)
            const string sqlMarkCase = @"
update ged.retention_case
set status = case when status='APPROVED' then 'EXECUTED' else status end,
    closed_at = coalesce(closed_at, now()),
    closed_by = coalesce(closed_by, @userId)
where tenant_id=@tenantId and id=@caseId;";

            await conn.ExecuteAsync(sqlMarkCase, new { tenantId, caseId, userId }, tx);

            await tx.CommitAsync(ct);

            return new ExecuteCaseResult(executed, blocked);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteCaseAsync failed. Tenant={TenantId} Case={CaseId}", tenantId, caseId);
            throw;
        }
    }
}