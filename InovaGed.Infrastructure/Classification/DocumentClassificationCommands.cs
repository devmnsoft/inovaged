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

    // =========================================================
    // AUDITORIA (requer tabela ged.document_classification_audit)
    // =========================================================
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
);
";

    // =========================================================
    // SNAPSHOT (before/after)
    // =========================================================
    private const string SnapshotHeadSql = @"
SELECT
  d.id                     AS ""DocumentId"",
  dc.document_version_id   AS ""DocumentVersionId"",
  dc.document_type_id      AS ""DocumentTypeId"",
  dt.name                  AS ""DocumentTypeName"",
  dc.confidence            AS ""Confidence"",
  dc.method                AS ""Method"",
  dc.summary               AS ""Summary"",
  dc.classified_at         AS ""ClassifiedAt"",
  dc.classified_by         AS ""ClassifiedBy"",

  dc.suggested_type_id     AS ""SuggestedTypeId"",
  dts.name                 AS ""SuggestedTypeName"",
  dc.suggested_confidence  AS ""SuggestedConfidence"",
  dc.suggested_summary     AS ""SuggestedSummary"",
  dc.suggested_at          AS ""SuggestedAt""
FROM ged.document d
LEFT JOIN ged.document_classification dc
  ON dc.document_id = d.id
LEFT JOIN ged.document_type dt
  ON dt.id = dc.document_type_id
 AND dt.tenant_id = @TenantId
LEFT JOIN ged.document_type dts
  ON dts.id = dc.suggested_type_id
 AND dts.tenant_id = @TenantId
WHERE d.tenant_id = @TenantId
  AND d.id = @DocumentId
  AND d.status <> 'DELETED'
LIMIT 1;
";

    private const string SnapshotTagsSql = @"
SELECT t.name
FROM ged.document_tag x
JOIN ged.tag t
  ON t.id = x.tag_id
 AND t.tenant_id = x.tenant_id
WHERE x.tenant_id = @TenantId
  AND x.document_id = @DocumentId
ORDER BY lower(t.name);
";

    private const string SnapshotMetaSql = @"
SELECT key, value
FROM ged.document_metadata
WHERE tenant_id = @TenantId
  AND document_id = @DocumentId
ORDER BY lower(key);
";

    private sealed class SnapshotHeadRow
    {
        public Guid DocumentId { get; set; }
        public Guid? DocumentVersionId { get; set; }
        public Guid? DocumentTypeId { get; set; }
        public string? DocumentTypeName { get; set; }
        public decimal? Confidence { get; set; }
        public string? Method { get; set; }
        public string? Summary { get; set; }
        public DateTimeOffset? ClassifiedAt { get; set; }
        public Guid? ClassifiedBy { get; set; }

        public Guid? SuggestedTypeId { get; set; }
        public string? SuggestedTypeName { get; set; }
        public decimal? SuggestedConfidence { get; set; }
        public string? SuggestedSummary { get; set; }
        public DateTimeOffset? SuggestedAt { get; set; }
    }

    private sealed class ClassificationSnapshot
    {
        public Guid DocumentId { get; set; }

        public Guid? DocumentVersionId { get; set; }

        public Guid? DocumentTypeId { get; set; }
        public string? DocumentTypeName { get; set; }
        public decimal? Confidence { get; set; }
        public string? Method { get; set; }
        public string? Summary { get; set; }
        public DateTimeOffset? ClassifiedAt { get; set; }
        public Guid? ClassifiedBy { get; set; }

        public List<string> Tags { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public SuggestionPart? Suggestion { get; set; }

        public sealed class SuggestionPart
        {
            public Guid? SuggestedTypeId { get; set; }
            public string? SuggestedTypeName { get; set; }
            public decimal? SuggestedConfidence { get; set; }
            public string? SuggestedSummary { get; set; }
            public DateTimeOffset? SuggestedAt { get; set; }
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private async Task<string?> BuildSnapshotJsonAsync(
        IDbConnection con, IDbTransaction tx,
        Guid tenantId, Guid documentId, CancellationToken ct)
    {
        var head = await con.QueryFirstOrDefaultAsync<SnapshotHeadRow>(
            new CommandDefinition(
                SnapshotHeadSql,
                new { TenantId = tenantId, DocumentId = documentId },
                transaction: tx,
                cancellationToken: ct));

        if (head is null) return null;

        var tags = (await con.QueryAsync<string>(
                new CommandDefinition(
                    SnapshotTagsSql,
                    new { TenantId = tenantId, DocumentId = documentId },
                    transaction: tx,
                    cancellationToken: ct)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var metaPairs = await con.QueryAsync<(string key, string value)>(
            new CommandDefinition(
                SnapshotMetaSql,
                new { TenantId = tenantId, DocumentId = documentId },
                transaction: tx,
                cancellationToken: ct));

        var meta = metaPairs
            .Where(x => !string.IsNullOrWhiteSpace(x.key))
            .GroupBy(x => x.key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (g.Last().value ?? "").Trim(),
                StringComparer.OrdinalIgnoreCase);

        var snap = new ClassificationSnapshot
        {
            DocumentId = head.DocumentId,
            DocumentVersionId = head.DocumentVersionId,
            DocumentTypeId = head.DocumentTypeId,
            DocumentTypeName = head.DocumentTypeName,
            Confidence = head.Confidence,
            Method = string.IsNullOrWhiteSpace(head.Method) ? null : head.Method,
            Summary = head.Summary,
            ClassifiedAt = head.ClassifiedAt,
            ClassifiedBy = head.ClassifiedBy,
            Tags = tags,
            Metadata = meta,
            Suggestion = (head.SuggestedTypeId.HasValue || head.SuggestedConfidence.HasValue || !string.IsNullOrWhiteSpace(head.SuggestedSummary))
                ? new ClassificationSnapshot.SuggestionPart
                {
                    SuggestedTypeId = head.SuggestedTypeId,
                    SuggestedTypeName = head.SuggestedTypeName,
                    SuggestedConfidence = head.SuggestedConfidence,
                    SuggestedSummary = head.SuggestedSummary,
                    SuggestedAt = head.SuggestedAt
                }
                : null
        };

        return JsonSerializer.Serialize(snap, JsonOpts);
    }

    // =========================================================
    // HELPERS: versão atual do documento (document_version_id NOT NULL)
    // =========================================================
    private const string GetLatestVersionSql = @"
SELECT id
FROM ged.document_versions
WHERE tenant_id = @TenantId
  AND document_id = @DocumentId
ORDER BY created_at DESC
LIMIT 1;
";

    private async Task<Guid> GetLatestVersionIdAsync(
        IDbConnection con, IDbTransaction tx,
        Guid tenantId, Guid documentId, CancellationToken ct)
    {
        var id = await con.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                GetLatestVersionSql,
                new { TenantId = tenantId, DocumentId = documentId },
                transaction: tx,
                cancellationToken: ct));

        if (!id.HasValue || id.Value == Guid.Empty)
            throw new InvalidOperationException($"Documento sem versão. Não é possível salvar classificação. DocumentId={documentId}");

        return id.Value;
    }

    // =========================================================
    // HELPERS: tags (sem UNIQUE, então faz SELECT e INSERT)
    // =========================================================
    private const string FindTagIdSql = @"
SELECT id
FROM ged.tag
WHERE tenant_id = @TenantId
  AND reg_status = 'A'
  AND lower(name) = lower(@Name)
LIMIT 1;
";

    private const string InsertTagSql = @"
INSERT INTO ged.tag (id, tenant_id, name, color, reg_date, reg_status)
VALUES (@Id, @TenantId, @Name, NULL, now(), 'A');
";

    private async Task<Guid> GetOrCreateTagIdAsync(
        IDbConnection con, IDbTransaction tx,
        Guid tenantId, string name, CancellationToken ct)
    {
        var existing = await con.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                FindTagIdSql,
                new { TenantId = tenantId, Name = name },
                transaction: tx,
                cancellationToken: ct));

        if (existing.HasValue && existing.Value != Guid.Empty)
            return existing.Value;

        var newId = Guid.NewGuid();

        await con.ExecuteAsync(
            new CommandDefinition(
                InsertTagSql,
                new { Id = newId, TenantId = tenantId, Name = name },
                transaction: tx,
                cancellationToken: ct));

        return newId;
    }

    // =========================================================
    // COMMANDS
    // =========================================================

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
            // BEFORE
            var beforeJson = await BuildSnapshotJsonAsync(con, tx, tenantId, documentId, ct) ?? "{}";

            // version obrigatória
            var versionId = await GetLatestVersionIdAsync(con, tx, tenantId, documentId, ct);

            // UPSERT classificação (PK = document_id)
            const string upsertSql = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id, document_version_id,
  document_type_id, confidence, method, summary,
  classified_at, classified_by, reg_status, source
)
VALUES
(
  @DocumentId, @TenantId, @DocumentVersionId,
  @DocumentTypeId, NULL, 'MANUAL', NULL,
  now(), @UserId, 'A', 'WEB'
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
  reg_status = 'A',
  source = 'WEB';
";
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

            // ---------------------------
            // TAGS (method='MANUAL')
            // ---------------------------
            const string delTagsSql = @"
DELETE FROM ged.document_tag
WHERE tenant_id=@TenantId AND document_id=@DocumentId AND method='MANUAL';
";
            await con.ExecuteAsync(
                new CommandDefinition(
                    delTagsSql,
                    new { TenantId = tenantId, DocumentId = documentId },
                    transaction: tx,
                    cancellationToken: ct));

            var cleanedTags = (tags ?? Array.Empty<string>())
                .Select(t => (t ?? "").Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanedTags.Count > 0)
            {
                const string insDocTagSql = @"
INSERT INTO ged.document_tag
(document_id, tag_id, tenant_id, assigned_by, assigned_at, method)
VALUES
(@DocumentId, @TagId, @TenantId, @UserId, now(), 'MANUAL');
";

                foreach (var tagName in cleanedTags)
                {
                    var tagId = await GetOrCreateTagIdAsync(con, tx, tenantId, tagName, ct);

                    await con.ExecuteAsync(
                        new CommandDefinition(
                            insDocTagSql,
                            new
                            {
                                TenantId = tenantId,
                                DocumentId = documentId,
                                TagId = tagId,
                                UserId = userId
                            },
                            transaction: tx,
                            cancellationToken: ct));
                }
            }

            // ---------------------------
            // METADATA (method='MANUAL')
            // ---------------------------
            const string delMetaSql = @"
DELETE FROM ged.document_metadata
WHERE tenant_id=@TenantId AND document_id=@DocumentId AND method='MANUAL';
";
            await con.ExecuteAsync(
                new CommandDefinition(
                    delMetaSql,
                    new { TenantId = tenantId, DocumentId = documentId },
                    transaction: tx,
                    cancellationToken: ct));

            if (metadata is { Count: > 0 })
            {
                const string insMetaSql = @"
INSERT INTO ged.document_metadata
(document_id, tenant_id, key, value, confidence, method, extracted_at)
VALUES
(@DocumentId, @TenantId, @Key, @Value, NULL, 'MANUAL', now());
";

                foreach (var kv in metadata)
                {
                    var k = (kv.Key ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(k)) continue;

                    await con.ExecuteAsync(
                        new CommandDefinition(
                            insMetaSql,
                            new
                            {
                                TenantId = tenantId,
                                DocumentId = documentId,
                                Key = k,
                                Value = kv.Value ?? ""
                            },
                            transaction: tx,
                            cancellationToken: ct));
                }
            }

            // AFTER
            var afterJson = await BuildSnapshotJsonAsync(con, tx, tenantId, documentId, ct) ?? "{}";

            // AUDIT
            await con.ExecuteAsync(
                new CommandDefinition(
                    InsertAuditSql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        UserId = userId,
                        Action = "MANUAL_SAVE",
                        Method = "MANUAL",
                        BeforeJson = beforeJson,
                        AfterJson = afterJson,
                        Source = "WEB"
                    },
                    transaction: tx,
                    cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            _logger.LogError(ex, "Erro SaveManualAsync | Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
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
            var beforeJson = await BuildSnapshotJsonAsync(con, tx, tenantId, documentId, ct) ?? "{}";
            var versionId = await GetLatestVersionIdAsync(con, tx, tenantId, documentId, ct);

            const string sql = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id, document_version_id,
  document_type_id, confidence, method, summary,
  classified_at, classified_by, reg_status, source,
  suggested_type_id, suggested_confidence, suggested_summary, suggested_at,
  suggested_conf
)
VALUES
(
  @DocumentId, @TenantId, @DocumentVersionId,
  @SuggestedTypeId, @SuggestedConfidence, 'SUGGESTION', @SuggestedSummary,
  now(), @UserId, 'A', 'WEB',
  @SuggestedTypeId, @SuggestedConfidence, @SuggestedSummary, now(),
  @SuggestedConfidence
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
  reg_status = 'A',
  source = 'WEB',

  suggested_type_id = @SuggestedTypeId,
  suggested_confidence = @SuggestedConfidence,
  suggested_summary = @SuggestedSummary,
  suggested_at = now(),
  suggested_conf = @SuggestedConfidence;
";
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

            var afterJson = await BuildSnapshotJsonAsync(con, tx, tenantId, documentId, ct) ?? "{}";

            await con.ExecuteAsync(
                new CommandDefinition(
                    InsertAuditSql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        UserId = userId,
                        Action = "APPLY_SUGGESTION",
                        Method = "SUGGESTION",
                        BeforeJson = beforeJson,
                        AfterJson = afterJson,
                        Source = "WEB"
                    },
                    transaction: tx,
                    cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            _logger.LogError(ex, "Erro ApplySuggestionAsync | Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
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
            var versionId = await GetLatestVersionIdAsync(con, tx, tenantId, documentId, ct);

            const string sql = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id, document_version_id,
  reg_status, source,
  suggested_type_id, suggested_confidence, suggested_summary, suggested_at,
  suggested_conf
)
VALUES
(
  @DocumentId, @TenantId, @DocumentVersionId,
  'A', 'WEB',
  @TypeId, @Conf, @Summary, now(),
  @Conf
)
ON CONFLICT (document_id)
DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  document_version_id = EXCLUDED.document_version_id,
  reg_status = 'A',
  source = 'WEB',
  suggested_type_id = @TypeId,
  suggested_confidence = @Conf,
  suggested_summary = @Summary,
  suggested_at = now(),
  suggested_conf = @Conf;
";
            await con.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        DocumentVersionId = versionId,
                        TypeId = suggestedTypeId,
                        Conf = suggestedConfidence,
                        Summary = suggestedSummary
                    },
                    transaction: tx,
                    cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            _logger.LogError(ex, "Erro SaveSuggestionOnlyAsync | Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            throw;
        }
    }
}
