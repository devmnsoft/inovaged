using System.Data;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using InovaGed.Application.Security;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentClassificationCommands : IDocumentClassificationCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentClassificationCommands> _logger;
    private readonly IRetentionRecalcService? _retention;
    private readonly IAbacAuthorizationService _authorization;
    private readonly IRetentionJobRepository _retentionJobs;

    public DocumentClassificationCommands(
        IDbConnectionFactory db,
        ILogger<DocumentClassificationCommands> logger,
        IRetentionJobRepository retentionJobs,
        IAbacAuthorizationService authorization,
        IRetentionRecalcService? retention = null)
    {
        _db = db;
        _logger = logger;
        _retention = retention;
        _authorization = authorization;
        _retentionJobs = retentionJobs;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string InsertAuditSql = @"
INSERT INTO ged.document_classification_audit
(
  id, tenant_id, document_id, user_id,
  action, method,
  before_json, after_json,
  source, created_at, reg_status
)
VALUES
(
  gen_random_uuid(), @TenantId, @DocumentId, @UserId,
  @Action, @Method,
  @BeforeJson::jsonb, @AfterJson::jsonb,
  @Source, now(), 'A'
);";

    private const string SnapshotSql = @"
SELECT jsonb_build_object(
    'documentId', d.id,
    'documentTypeId', dc.document_type_id,
    'documentTypeName', dt.name,
    'confidence', dc.confidence,
    'method', dc.method,
    'summary', dc.summary,
    'classifiedAt', dc.classified_at,
    'classifiedBy', dc.classified_by,
    'suggestedTypeId', dc.suggested_type_id,
    'suggestedTypeName', dts.name,
    'suggestedConfidence', dc.suggested_confidence,
    'suggestedSummary', dc.suggested_summary,
    'suggestedAt', dc.suggested_at
)::text
FROM ged.document d
LEFT JOIN ged.document_classification dc
       ON dc.tenant_id = d.tenant_id
      AND dc.document_id = d.id
      AND dc.reg_status = 'A'
LEFT JOIN ged.document_type dt
       ON dt.tenant_id = d.tenant_id
      AND dt.id = dc.document_type_id
LEFT JOIN ged.document_type dts
       ON dts.tenant_id = d.tenant_id
      AND dts.id = dc.suggested_type_id
WHERE d.tenant_id = @TenantId
  AND d.id = @DocumentId
LIMIT 1;";

    private const string GetLatestVersionSql = @"
SELECT COALESCE(d.current_version_id, v.id)
FROM ged.document d
LEFT JOIN LATERAL (
    SELECT id
    FROM ged.document_version
    WHERE tenant_id = d.tenant_id
      AND document_id = d.id
    ORDER BY created_at DESC
    LIMIT 1
) v ON true
WHERE d.tenant_id = @TenantId
  AND d.id = @DocumentId
LIMIT 1;";

    private async Task<string> SnapshotAsync(
        IDbConnection con,
        IDbTransaction tx,
        Guid tenantId,
        Guid documentId,
        CancellationToken ct)
    {
        var json = await con.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                SnapshotSql,
                new { TenantId = tenantId, DocumentId = documentId },
                transaction: tx,
                cancellationToken: ct));

        return string.IsNullOrWhiteSpace(json) ? "{}" : json;
    }

    private async Task<Guid> GetLatestVersionIdAsync(
        IDbConnection con,
        IDbTransaction tx,
        Guid tenantId,
        Guid documentId,
        CancellationToken ct)
    {
        var versionId = await con.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                GetLatestVersionSql,
                new { TenantId = tenantId, DocumentId = documentId },
                transaction: tx,
                cancellationToken: ct));

        if (!versionId.HasValue || versionId.Value == Guid.Empty)
            throw new InvalidOperationException("Documento sem versão. Não é possível classificar.");

        return versionId.Value;
    }

    public async Task SaveManualAsync(
        Guid tenantId,
        Guid documentId,
        Guid? documentTypeId,
        Guid? userId,
        IReadOnlyList<string>? tags,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
    {
        await using var con = await _db.OpenAsync(ct);
        await using var tx = con.BeginTransaction();

        try
        {
            var beforeJson = await SnapshotAsync(con, tx, tenantId, documentId, ct);
            var versionId = await GetLatestVersionIdAsync(con, tx, tenantId, documentId, ct);

            const string upsertSql = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id, document_version_id,
  document_type_id, confidence, method, summary,
  classified_at, classified_by,
  source, updated_at, reg_status
)
VALUES
(
  @DocumentId, @TenantId, @DocumentVersionId,
  @DocumentTypeId, NULL, 'MANUAL', NULL,
  now(), @UserId,
  'WEB', now(), 'A'
)
ON CONFLICT (document_id)
DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  document_version_id = EXCLUDED.document_version_id,
  document_type_id = EXCLUDED.document_type_id,
  confidence = NULL,
  method = 'MANUAL',
  summary = NULL,
  classified_at = now(),
  classified_by = @UserId,
  source = 'WEB',
  updated_at = now(),
  reg_status = 'A';";

            await con.ExecuteAsync(
                new CommandDefinition(
                    upsertSql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        DocumentVersionId = versionId,
                        DocumentTypeId = documentTypeId,
                        UserId = userId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            const string syncDocumentSql = @"
UPDATE ged.document
SET type_id = @DocumentTypeId,
    updated_at = now(),
    updated_by = @UserId
WHERE tenant_id = @TenantId
  AND id = @DocumentId;";

            await con.ExecuteAsync(
                new CommandDefinition(
                    syncDocumentSql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        DocumentTypeId = documentTypeId,
                        UserId = userId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            await SaveTagsAsync(con, tx, tenantId, documentId, userId, tags, "MANUAL", ct);
            await SaveMetadataAsync(con, tx, tenantId, documentId, metadata, "MANUAL", ct);

            var afterJson = await SnapshotAsync(con, tx, tenantId, documentId, ct);

            await InsertAuditAsync(
                con, tx, tenantId, documentId, userId,
                "MANUAL_SAVE", "MANUAL", beforeJson, afterJson, "WEB", ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { }

            _logger.LogError(
                ex,
                "Erro ao salvar classificação manual. Tenant={TenantId} Document={DocumentId}",
                tenantId,
                documentId);

            throw;
        }
    }

    public async Task<SaveManualIntegratedResult> SaveManualIntegratedAsync(
        SaveManualIntegratedCommand command,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (command.DocumentId == Guid.Empty)
            return new(false, false, false, false, false, false, "ID do documento inválido.", "INVALID_ID");

        if (command.TenantId == Guid.Empty)
            return new(false, false, false, false, false, false, "Tenant inválido.", "INVALID_TENANT");

        if (!command.IsSystemExecution && (!command.UserId.HasValue || command.UserId.Value == Guid.Empty))
            return new(false, false, false, false, false, false, "Identidade obrigatória para editar classificação.", "IDENTITY_REQUIRED");

        if (command.IsSystemExecution && string.IsNullOrWhiteSpace(command.SystemExecutionReason))
            return new(false, false, false, false, false, false, "Execução de sistema exige justificativa auditável.", "SYSTEM_REASON_REQUIRED");

        if (!command.IsSystemExecution)
        {
            var allowed = await _authorization.FilterDocumentsAsync(command.TenantId, command.UserId.Value, new[] { command.DocumentId }, "EDIT", ct);
            if (!allowed.Contains(command.DocumentId))
                return new(false, false, false, false, false, false, "Acesso negado para edição deste documento.", "FORBIDDEN");
        }

        await using var con = await _db.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        try
        {
            const string lockSql = @"
select id, current_version_id as CurrentVersionId, classification_id as ClassificationId, classification_version_id as ClassificationVersionId, type_id as TypeId
from ged.document
where tenant_id = @TenantId and id = @DocumentId and coalesce(reg_status, 'A') = 'A'
for update;";

            var doc = await con.QueryFirstOrDefaultAsync<(Guid Id, Guid? CurrentVersionId, Guid? ClassificationId, Guid? ClassificationVersionId, Guid? TypeId)>(
                new CommandDefinition(lockSql, new { command.TenantId, command.DocumentId }, tx, cancellationToken: ct));

            if (doc.Id == Guid.Empty)
            {
                await tx.RollbackAsync(ct);
                return new(false, false, false, false, false, false, "Documento não encontrado ou inativo.", "NOT_FOUND");
            }

            var beforeJson = await SnapshotAsync(con, tx, command.TenantId, command.DocumentId, ct);
            var versionId = doc.CurrentVersionId ?? await GetLatestVersionIdAsync(con, tx, command.TenantId, command.DocumentId, ct);

            bool classificationChanged = false;
            bool typeChanged = false;
            bool tagsChanged = false;
            bool metadataChanged = false;
            Guid? pendingRecalcId = null;

            // Classificação
            if (command.ClassificationAction == ClassificationEditAction.Replace)
            {
                if (command.ClassificationId.HasValue && command.ClassificationId.Value != Guid.Empty)
                {
                    const string checkClassSql = @"
select v.id
from ged.classification_plan_version_item i
join ged.classification_plan_version v on v.tenant_id = i.tenant_id and v.id = i.version_id
where i.tenant_id = @TenantId
  and i.classification_id = @ClassificationId
  and coalesce(i.is_active, true)
  and coalesce(v.reg_status, 'A') = 'A'
  and v.version_no = (
    select max(version_no)
    from ged.classification_plan_version
    where tenant_id = @TenantId and coalesce(reg_status, 'A') = 'A'
  );";

                    var targetVersionId = await con.ExecuteScalarAsync<Guid?>(
                        new CommandDefinition(checkClassSql, new { command.TenantId, command.ClassificationId }, tx, cancellationToken: ct));

                    if (targetVersionId == null || targetVersionId == Guid.Empty)
                    {
                        await tx.RollbackAsync(ct);
                        return new(false, false, false, false, false, false, "A classificação informada não pertence ao plano vigente ou está inativa.", "INVALID_CLASSIFICATION");
                    }

                    const string updateClassSql = @"
update ged.document
set classification_id = @ClassificationId,
    classification_version_id = @VersionId,
    updated_at = now(),
    updated_by = @UserId
where tenant_id = @TenantId and id = @DocumentId;";

                    await con.ExecuteAsync(new CommandDefinition(updateClassSql, new
                    {
                        command.TenantId,
                        command.DocumentId,
                        command.ClassificationId,
                        VersionId = targetVersionId,
                        command.UserId
                    }, tx, cancellationToken: ct));

                    const string upsertClassItemSql = @"
insert into ged.document_classification
  (document_id, tenant_id, document_version_id, classification_id, classification_version_id, confidence, method, summary, classified_at, classified_by, source, updated_at, reg_status)
values
  (@DocumentId, @TenantId, @DocumentVersionId, @ClassificationId, @ClassificationVersionId, null, 'MANUAL', null, now(), @UserId, 'WEB', now(), 'A')
on conflict (document_id)
do update set
  tenant_id = excluded.tenant_id,
  document_version_id = excluded.document_version_id,
  classification_id = excluded.classification_id,
  classification_version_id = excluded.classification_version_id,
  method = 'MANUAL',
  classified_at = now(),
  classified_by = @UserId,
  source = 'WEB',
  updated_at = now(),
  reg_status = 'A';";

                    await con.ExecuteAsync(new CommandDefinition(upsertClassItemSql, new
                    {
                        command.TenantId,
                        command.DocumentId,
                        DocumentVersionId = versionId,
                        command.ClassificationId,
                        ClassificationVersionId = targetVersionId,
                        command.UserId
                    }, tx, cancellationToken: ct));

                    pendingRecalcId = await _retentionJobs.EnqueueRecalculateAsync(con, tx, command.TenantId, command.DocumentId, "MANUAL_SAVE_RECALC", ct);
                    classificationChanged = true;
                }
                else
                {
                    await tx.RollbackAsync(ct);
                    return new(false, false, false, false, false, false, "Informe uma classificação vigente para substituir.", "INVALID_CLASSIFICATION_ACTION");
                }
            }
            else if (command.ClassificationAction == ClassificationEditAction.Remove)
            {
                if (!command.ConfirmClassificationRemoval)
                {
                    await tx.RollbackAsync(ct);
                    return new(false, false, false, false, false, false, "Confirme explicitamente a remoção da classificação arquivística.", "REMOVAL_CONFIRMATION_REQUIRED");
                }

                    const string clearDocClassSql = @"
update ged.document
set classification_id = null,
    classification_version_id = null,
    updated_at = now(),
    updated_by = @UserId
where tenant_id = @TenantId and id = @DocumentId;";

                    await con.ExecuteAsync(new CommandDefinition(clearDocClassSql, new
                    {
                        command.TenantId,
                        command.DocumentId,
                        command.UserId
                    }, tx, cancellationToken: ct));

                    const string clearClassItemSql = @"
update ged.document_classification
set classification_id = null,
    classification_version_id = null,
    updated_at = now(),
    classified_by = @UserId
where tenant_id = @TenantId and document_id = @DocumentId;";

                    await con.ExecuteAsync(new CommandDefinition(clearClassItemSql, new
                    {
                        command.TenantId,
                        command.DocumentId,
                        command.UserId
                    }, tx, cancellationToken: ct));

                    pendingRecalcId = await _retentionJobs.EnqueueRecalculateAsync(con, tx, command.TenantId, command.DocumentId, "MANUAL_SAVE_RECALC", ct);
                    classificationChanged = true;
            }

            // Tipo Documental
            if (command.TypeAction == DocumentTypeEditAction.Replace)
            {
                if (command.DocumentTypeId.HasValue && command.DocumentTypeId.Value != Guid.Empty)
                {
                    var typeExists = await con.ExecuteScalarAsync<bool>(new CommandDefinition(
                        "select exists(select 1 from ged.document_type where tenant_id = @TenantId and id = @Id and coalesce(reg_status,'A')='A');",
                        new { command.TenantId, Id = command.DocumentTypeId.Value }, tx, cancellationToken: ct));

                    if (!typeExists)
                    {
                        await tx.RollbackAsync(ct);
                        return new(false, classificationChanged, false, false, false, false, "Tipo documental não encontrado.", "INVALID_TYPE");
                    }

                    await con.ExecuteAsync(new CommandDefinition(@"
update ged.document
set type_id = @TypeId,
    updated_at = now(),
    updated_by = @UserId
where tenant_id = @TenantId and id = @DocumentId;", new { command.TenantId, command.DocumentId, TypeId = command.DocumentTypeId.Value, command.UserId }, tx, cancellationToken: ct));

                    await con.ExecuteAsync(new CommandDefinition(@"
insert into ged.document_classification
  (document_id, tenant_id, document_version_id, document_type_id, method, classified_at, classified_by, source, updated_at, reg_status)
values
  (@DocumentId, @TenantId, @DocumentVersionId, @TypeId, 'MANUAL', now(), @UserId, 'WEB', now(), 'A')
on conflict (document_id)
do update set
  tenant_id = excluded.tenant_id,
  document_version_id = excluded.document_version_id,
  document_type_id = excluded.document_type_id,
  method = 'MANUAL',
  classified_at = now(),
  classified_by = @UserId,
  source = 'WEB',
  updated_at = now(),
  reg_status = 'A';", new { command.TenantId, command.DocumentId, DocumentVersionId = versionId, TypeId = command.DocumentTypeId.Value, command.UserId }, tx, cancellationToken: ct));

                    typeChanged = true;
                }
                else
                {
                    await tx.RollbackAsync(ct);
                    return new(false, false, false, false, false, false, "Informe um tipo documental ativo para substituir.", "INVALID_TYPE_ACTION");
                }
            }
            else if (command.TypeAction == DocumentTypeEditAction.Remove)
            {
                    await con.ExecuteAsync(new CommandDefinition(@"
update ged.document
set type_id = null,
    updated_at = now(),
    updated_by = @UserId
where tenant_id = @TenantId and id = @DocumentId;", new { command.TenantId, command.DocumentId, command.UserId }, tx, cancellationToken: ct));

                    await con.ExecuteAsync(new CommandDefinition(@"
update ged.document_classification
set document_type_id = null,
    updated_at = now(),
    classified_by = @UserId
where tenant_id = @TenantId and document_id = @DocumentId;", new { command.TenantId, command.DocumentId, command.UserId }, tx, cancellationToken: ct));

                    typeChanged = true;
            }

            // Tags
            if (command.HasTags)
            {
                await SaveTagsAsync(con, tx, command.TenantId, command.DocumentId, command.UserId, command.Tags ?? Array.Empty<string>(), "MANUAL", ct);
                tagsChanged = true;
            }

            // Metadados
            if (command.HasMetadata)
            {
                const string delMetaSql = @"
delete from ged.document_metadata
where tenant_id = @TenantId
  and document_id = @DocumentId
  and coalesce(method, 'MANUAL') = 'MANUAL';";

                await con.ExecuteAsync(new CommandDefinition(delMetaSql, new { command.TenantId, command.DocumentId }, tx, cancellationToken: ct));

                if (command.Metadata != null && command.Metadata.Count > 0)
                {
                    await SaveMetadataAsync(con, tx, command.TenantId, command.DocumentId, command.Metadata, "MANUAL", ct);
                }
                metadataChanged = true;
            }

            if (classificationChanged || typeChanged || tagsChanged || metadataChanged)
            {
                var afterJson = await SnapshotAsync(con, tx, command.TenantId, command.DocumentId, ct);
                await InsertAuditAsync(con, tx, command.TenantId, command.DocumentId, command.UserId,
                    "MANUAL_SAVE", "MANUAL", beforeJson, afterJson, "WEB", ct);
            }

            await tx.CommitAsync(ct);

            bool recalcOk = false;
            if (classificationChanged && _retention != null)
            {
                try
                {
                    var calculated = await _retention.RunOneAsync(command.TenantId, command.DocumentId, 30, ct);
                    if (calculated == 1)
                    {
                        recalcOk = true;
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        if (pendingRecalcId is Guid id)
                            await _retentionJobs.ResolvePendingRecalcAsync(command.TenantId, id, cleanup.Token);
                    }
                }
                catch (Exception rex)
                {
                    _logger.LogWarning(rex, "Recálculo imediato de temporalidade pendente de worker. DocumentId={DocumentId}", command.DocumentId);
                }
            }

            return new SaveManualIntegratedResult(
                Success: true,
                ClassificationChanged: classificationChanged,
                TypeChanged: typeChanged,
                TagsChanged: tagsChanged,
                MetadataChanged: metadataChanged,
                RetentionRecalculated: recalcOk,
                Message: classificationChanged
                    ? (recalcOk ? "Classificação e dados salvos com sucesso. Temporalidade recalculada." : "Classificação e dados salvos com sucesso. Temporalidade enfileirada para recálculo.")
                    : "Dados salvos com sucesso."
            );
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { }
            _logger.LogError(ex, "Erro ao salvar classificação manual integrada. Tenant={TenantId} DocumentId={DocumentId}", command.TenantId, command.DocumentId);
            return new(false, false, false, false, false, false, "Falha interna ao salvar alterações.", "UNEXPECTED_ERROR");
        }
    }

    public async Task ApplySuggestionAsync(
        Guid tenantId,
        Guid documentId,
        Guid suggestedTypeId,
        decimal? suggestedConfidence,
        string? suggestedSummary,
        Guid? userId,
        CancellationToken ct)
    {
        await using var con = await _db.OpenAsync(ct);
        await using var tx = con.BeginTransaction();

        try
        {
            var beforeJson = await SnapshotAsync(con, tx, tenantId, documentId, ct);
            var versionId = await GetLatestVersionIdAsync(con, tx, tenantId, documentId, ct);

            const string sql = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id, document_version_id,
  document_type_id, confidence, method, summary,
  classified_at, classified_by,
  suggested_type_id, suggested_confidence, suggested_summary, suggested_at, suggested_conf,
  source, updated_at, reg_status
)
VALUES
(
  @DocumentId, @TenantId, @DocumentVersionId,
  @SuggestedTypeId, @SuggestedConfidence, 'SUGGESTION', @SuggestedSummary,
  now(), @UserId,
  @SuggestedTypeId, @SuggestedConfidence, @SuggestedSummary, now(), @SuggestedConfidence,
  'WEB', now(), 'A'
)
ON CONFLICT (document_id)
DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  document_version_id = EXCLUDED.document_version_id,
  document_type_id = @SuggestedTypeId,
  confidence = @SuggestedConfidence,
  method = 'SUGGESTION',
  summary = @SuggestedSummary,
  classified_at = now(),
  classified_by = @UserId,
  suggested_type_id = @SuggestedTypeId,
  suggested_confidence = @SuggestedConfidence,
  suggested_summary = @SuggestedSummary,
  suggested_at = now(),
  suggested_conf = @SuggestedConfidence,
  source = 'WEB',
  updated_at = now(),
  reg_status = 'A';";

            await con.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        DocumentVersionId = versionId,
                        SuggestedTypeId = suggestedTypeId,
                        SuggestedConfidence = suggestedConfidence,
                        SuggestedSummary = suggestedSummary,
                        UserId = userId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            const string syncDocumentSql = @"
UPDATE ged.document
SET type_id = @SuggestedTypeId,
    updated_at = now(),
    updated_by = @UserId
WHERE tenant_id = @TenantId
  AND id = @DocumentId;";

            await con.ExecuteAsync(
                new CommandDefinition(
                    syncDocumentSql,
                    new { TenantId = tenantId, DocumentId = documentId, SuggestedTypeId = suggestedTypeId, UserId = userId },
                    transaction: tx,
                    cancellationToken: ct));

            var afterJson = await SnapshotAsync(con, tx, tenantId, documentId, ct);

            await InsertAuditAsync(
                con, tx, tenantId, documentId, userId,
                "APPLY_SUGGESTION", "SUGGESTION", beforeJson, afterJson, "WEB", ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { }

            _logger.LogError(
                ex,
                "Erro ao aplicar sugestão. Tenant={TenantId} Document={DocumentId}",
                tenantId,
                documentId);

            throw;
        }
    }

    public async Task SaveSuggestionOnlyAsync(
        Guid tenantId,
        Guid documentId,
        Guid suggestedTypeId,
        decimal? suggestedConfidence,
        string? suggestedSummary,
        CancellationToken ct)
    {
        await using var con = await _db.OpenAsync(ct);
        await using var tx = con.BeginTransaction();

        try
        {
            var beforeJson = await SnapshotAsync(con, tx, tenantId, documentId, ct);
            var versionId = await GetLatestVersionIdAsync(con, tx, tenantId, documentId, ct);

            const string sql = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id, document_version_id,
  suggested_type_id, suggested_confidence, suggested_summary, suggested_at, suggested_conf,
  source, updated_at, reg_status
)
VALUES
(
  @DocumentId, @TenantId, @DocumentVersionId,
  @SuggestedTypeId, @SuggestedConfidence, @SuggestedSummary, now(), @SuggestedConfidence,
  'OCR', now(), 'A'
)
ON CONFLICT (document_id)
DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  document_version_id = EXCLUDED.document_version_id,
  suggested_type_id = @SuggestedTypeId,
  suggested_confidence = @SuggestedConfidence,
  suggested_summary = @SuggestedSummary,
  suggested_at = now(),
  suggested_conf = @SuggestedConfidence,
  source = COALESCE(ged.document_classification.source, 'OCR'),
  updated_at = now(),
  reg_status = 'A';";

            await con.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        DocumentVersionId = versionId,
                        SuggestedTypeId = suggestedTypeId,
                        SuggestedConfidence = suggestedConfidence,
                        SuggestedSummary = suggestedSummary
                    },
                    transaction: tx,
                    cancellationToken: ct));

            var afterJson = await SnapshotAsync(con, tx, tenantId, documentId, ct);

            await InsertAuditAsync(
                con, tx, tenantId, documentId, null,
                "OCR_SUGGESTION", "OCR", beforeJson, afterJson, "OCR_WORKER", ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { }

            _logger.LogError(
                ex,
                "Erro ao salvar sugestão OCR. Tenant={TenantId} Document={DocumentId}",
                tenantId,
                documentId);

            throw;
        }
    }

    private async Task InsertAuditAsync(
        IDbConnection con,
        IDbTransaction tx,
        Guid tenantId,
        Guid documentId,
        Guid? userId,
        string action,
        string method,
        string beforeJson,
        string afterJson,
        string source,
        CancellationToken ct)
    {
        await con.ExecuteAsync(
            new CommandDefinition(
                InsertAuditSql,
                new
                {
                    TenantId = tenantId,
                    DocumentId = documentId,
                    UserId = userId,
                    Action = action,
                    Method = method,
                    BeforeJson = beforeJson,
                    AfterJson = afterJson,
                    Source = source
                },
                transaction: tx,
                cancellationToken: ct));
    }

    private async Task SaveTagsAsync(
        IDbConnection con,
        IDbTransaction tx,
        Guid tenantId,
        Guid documentId,
        Guid? userId,
        IReadOnlyList<string> tags,
        string method,
        CancellationToken ct)
    {
        if (tags is null) return;
        const string deleteSql = @"
DELETE FROM ged.document_tag
WHERE tenant_id = @TenantId
  AND document_id = @DocumentId
  AND COALESCE(method, '') = @Method;";

        await con.ExecuteAsync(
            new CommandDefinition(
                deleteSql,
                new { TenantId = tenantId, DocumentId = documentId, Method = method },
                transaction: tx,
                cancellationToken: ct));

        var cleanTags = (tags ?? Array.Empty<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tag in cleanTags)
        {
            var tagId = await GetOrCreateTagAsync(con, tx, tenantId, tag, ct);

            const string insertDocTagSql = @"
INSERT INTO ged.document_tag
(document_id, tag_id, tenant_id, assigned_by, assigned_at, method)
VALUES
(@DocumentId, @TagId, @TenantId, @UserId, now(), @Method)
ON CONFLICT DO NOTHING;";

            await con.ExecuteAsync(
                new CommandDefinition(
                    insertDocTagSql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        TagId = tagId,
                        UserId = userId,
                        Method = method
                    },
                    transaction: tx,
                    cancellationToken: ct));
        }
    }

    private async Task<Guid> GetOrCreateTagAsync(
        IDbConnection con,
        IDbTransaction tx,
        Guid tenantId,
        string name,
        CancellationToken ct)
    {
        const string findSql = @"
SELECT id
FROM ged.tag
WHERE tenant_id = @TenantId
  AND reg_status = 'A'
  AND lower(name) = lower(@Name)
LIMIT 1;";

        var id = await con.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                findSql,
                new { TenantId = tenantId, Name = name },
                transaction: tx,
                cancellationToken: ct));

        if (id.HasValue && id.Value != Guid.Empty)
            return id.Value;

        var newId = Guid.NewGuid();

        const string insertSql = @"
INSERT INTO ged.tag (id, tenant_id, name, color, reg_date, reg_status)
VALUES (@Id, @TenantId, @Name, NULL, now(), 'A');";

        await con.ExecuteAsync(
            new CommandDefinition(
                insertSql,
                new { Id = newId, TenantId = tenantId, Name = name },
                transaction: tx,
                cancellationToken: ct));

        return newId;
    }

    private async Task SaveMetadataAsync(
       IDbConnection con,
       IDbTransaction tx,
       Guid tenantId,
       Guid documentId,
       IReadOnlyDictionary<string, string> metadata,
       string method,
       CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId inválido.", nameof(tenantId));

        if (documentId == Guid.Empty)
            throw new ArgumentException("DocumentId inválido.", nameof(documentId));

        method = string.IsNullOrWhiteSpace(method)
            ? "MANUAL"
            : method.Trim();

        /*
         IMPORTANTE:
         A tabela ged.document_metadata possui uma PK/unique chamada document_metadata_pkey.
         O erro 23505 acontece quando já existe metadado para a mesma chave do documento.
         Por isso usamos UPSERT real, sem DELETE prévio.
        */

        if (metadata is not { Count: > 0 })
            return;

        const string upsertSql = @"
INSERT INTO ged.document_metadata
(
    document_id,
    tenant_id,
    key,
    value,
    confidence,
    method,
    extracted_at
)
VALUES
(
    @DocumentId,
    @TenantId,
    @Key,
    @Value,
    NULL,
    @Method,
    now()
)
ON CONFLICT ON CONSTRAINT document_metadata_pkey
DO UPDATE SET
    tenant_id = EXCLUDED.tenant_id,
    value = EXCLUDED.value,
    confidence = EXCLUDED.confidence,
    method = EXCLUDED.method,
    extracted_at = now();";

        foreach (var kv in metadata)
        {
            var key = (kv.Key ?? "").Trim();

            if (string.IsNullOrWhiteSpace(key))
                continue;

            var value = kv.Value ?? "";

            await con.ExecuteAsync(
                new CommandDefinition(
                    upsertSql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        Key = key,
                        Value = value,
                        Method = method
                    },
                    transaction: tx,
                    cancellationToken: ct));
        }
    }
}
