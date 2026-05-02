using System.Data;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentClassificationCommands : IDocumentClassificationCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentClassificationCommands> _logger;

    public DocumentClassificationCommands(
        IDbConnectionFactory db,
        ILogger<DocumentClassificationCommands> logger)
    {
        _db = db;
        _logger = logger;
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
        IReadOnlyList<string> tags,
        IReadOnlyDictionary<string, string> metadata,
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