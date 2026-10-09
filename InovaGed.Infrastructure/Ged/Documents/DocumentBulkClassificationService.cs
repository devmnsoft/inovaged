using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Retention;
using InovaGed.Application.Security;
using InovaGed.Infrastructure.Retention;
using Npgsql;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class DocumentBulkClassificationService(
    IDbConnectionFactory db,
    IDocumentCommands commands,
    IRetentionRecalcService retention,
    IRetentionJobRepository retentionJobs,
    AssistedRetentionRecovery retentionRecovery,
    IAuditWriter audit,
    IAbacAuthorizationService authorization) : IDocumentBulkClassificationService
{
    private const string PendingReason = "BULK_CLASSIFICATION_RECALC";

    public async Task<DocumentBulkClassificationResult> ApplyAsync(Guid tenantId, Guid userId,
        IReadOnlyCollection<Guid> documentIds, Guid classificationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (documentIds.Count > 500)
            throw new ArgumentException("O lote deve conter no máximo 500 documentos.", nameof(documentIds));
        if (documentIds.Count == 0)
            return new(0, 0, 0, Array.Empty<DocumentBulkClassificationItem>());

        await using var connection = await db.OpenAsync(ct);
        var classificationExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
  select 1
  from ged.classification_plan_version_item i
  join ged.classification_plan_version v on v.tenant_id=i.tenant_id and v.id=i.version_id
  where i.tenant_id=@tenantId
    and i.classification_id=@classificationId
    and coalesce(i.is_active,true)
    and coalesce(v.reg_status,'A')='A'
    and v.version_no=(select max(version_no) from ged.classification_plan_version where tenant_id=@tenantId and coalesce(reg_status,'A')='A')
);
""", new { tenantId, classificationId }, cancellationToken: ct));
        if (!classificationExists) throw new ArgumentException("Classificação ativa não encontrada.");

        var items = new List<DocumentBulkClassificationItem>(documentIds.Count);
        var seen = new HashSet<Guid>();
        var cancelled = false;
        foreach (var id in documentIds)
        {
            if (cancelled || ct.IsCancellationRequested)
            {
                cancelled = true;
                items.Add(Failed(id, "Operação cancelada antes do processamento.", "CANCELLED"));
                continue;
            }

            if (id == Guid.Empty) { items.Add(Denied(id, "ID inválido.", "INVALID_ID")); continue; }
            if (!seen.Add(id)) { items.Add(Denied(id, "Documento duplicado no lote; processado apenas uma vez.", "DUPLICATE_ID")); continue; }

            try
            {
                items.Add(await ApplyOneAsync(connection, tenantId, userId, id, classificationId, ct));
                if (items[^1].Code == "RETENTION_CANCELLED") cancelled = true;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                items.Add(Failed(id, "Operação cancelada antes da confirmação.", "CANCELLED"));
            }
            catch (Exception ex)
            {
                var code = ex is PostgresException pg && !string.IsNullOrWhiteSpace(pg.SqlState)
                    ? "SQL_" + pg.SqlState
                    : "FAILED";
                items.Add(Failed(id, "Não foi possível classificar o documento.", code));
            }
        }

        var result = new DocumentBulkClassificationResult(documentIds.Count, items.Count(x => x.Success),
            items.Count(x => x.Status == "FAILED"), items);
        var auditStatus = "RECORDED";
        try
        {
            // Committed work must be audited even when the request was cancelled.
            var auditResult = await audit.WriteAsync(tenantId, userId, "DOCUMENT_BULK_CLASSIFIED", "DOCUMENT", null,
                "Classificação em massa concluída", null, null,
                new { requested = result.Requested, applied = result.Applied, pending = result.Pending, denied = result.Denied, failed = result.Failed, classificationId },
                CancellationToken.None);
            if (auditResult.IsFailure) auditStatus = "AUDIT_FAILED";
        }
        catch { auditStatus = "AUDIT_FAILED"; }
        return result with { AuditStatus = auditStatus };
    }

    private async Task<DocumentBulkClassificationItem> ApplyOneAsync(System.Data.Common.DbConnection connection,
        Guid tenantId, Guid userId, Guid id, Guid classificationId, CancellationToken ct)
    {
        // Authorization is evaluated per item, immediately before commit work, so revocations are honored.
        var allowed = await authorization.FilterDocumentsAsync(tenantId, userId, new[] { id }, "EDIT", ct);
        if (!allowed.Contains(id)) return Denied(id, "Documento não encontrado ou inacessível.", "NOT_FOUND");

        Guid pendingId;
        await using (var tx = await connection.BeginTransactionAsync(ct))
        {
            var locked = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
select id from ged.document where tenant_id=@tenantId and id=@id and coalesce(reg_status,'A')='A' for update;
""", new { tenantId, id }, tx, cancellationToken: ct));
            if (locked is null) return Denied(id, "Documento não encontrado ou inacessível.", "NOT_FOUND");

            var rows = await connection.ExecuteAsync(new CommandDefinition("""
update ged.document d
set classification_id=@classificationId, classification_version_id=v.id, updated_at=now(), updated_by=@userId
from ged.classification_plan_version v
where d.tenant_id=@tenantId and d.id=@id
  and v.tenant_id=@tenantId
  and coalesce(v.reg_status,'A')='A'
  and v.id=(select id from ged.classification_plan_version where tenant_id=@tenantId and coalesce(reg_status,'A')='A' order by version_no desc limit 1)
  and exists (select 1 from ged.classification_plan_version_item i
              where i.tenant_id=@tenantId and i.version_id=v.id and i.classification_id=@classificationId and coalesce(i.is_active,true));
""", new { tenantId, userId, id, classificationId }, tx, cancellationToken: ct));
            if (rows != 1) return Denied(id, "A classe não pertence mais ao plano vigente.", "PLAN_CHANGED");

            await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.document_classification
  (document_id, tenant_id, document_version_id, classification_id, classification_version_id, confidence, method, summary, classified_at, classified_by, source, updated_at, reg_status)
select d.id, d.tenant_id, d.current_version_id, d.classification_id, d.classification_version_id, null, 'MANUAL', 'Classificação aplicada no upload/lote', now(), @userId, 'UPLOAD_BULK_CLASSIFICATION', now(), 'A'
from ged.document d
where d.tenant_id=@tenantId and d.id=@id
on conflict (document_id)
do update set
  tenant_id=excluded.tenant_id,
  document_version_id=excluded.document_version_id,
  classification_id=excluded.classification_id,
  classification_version_id=excluded.classification_version_id,
  confidence=null,
  method='MANUAL',
  summary=excluded.summary,
  classified_at=now(),
  classified_by=@userId,
  source='UPLOAD_BULK_CLASSIFICATION',
  updated_at=now(),
  reg_status='A'
where ged.document_classification.tenant_id=excluded.tenant_id;
""", new { tenantId, userId, id }, tx, cancellationToken: ct));

            // Same transaction: the classification cannot commit without durable recalculation work.
            pendingId = await retentionJobs.EnqueueRecalculateAsync(connection, tx, tenantId, id, PendingReason, ct);
            await tx.CommitAsync(ct);
        }

        try
        {
            var recovered = await retentionRecovery.RunAsync(tenantId, pendingId, manual: true, ct);
            if (!recovered.Resolved) throw new InvalidOperationException("retention_pending");
        }
        catch (Exception ex)
        {
            var cancelledRecalc = ex is OperationCanceledException;
            return new(id, true, "Classificação aplicada; temporalidade pendente de recuperação.", "PENDING",
                cancelledRecalc ? "RETENTION_CANCELLED" : "RETENTION_PENDING");
        }
        return new(id, true, "Classificação aplicada.", "APPLIED", "APPLIED");
    }

    private static DocumentBulkClassificationItem Denied(Guid id, string message, string code) => new(id, false, message, "DENIED", code);
    private static DocumentBulkClassificationItem Failed(Guid id, string message, string code) => new(id, false, message, "FAILED", code);
}
