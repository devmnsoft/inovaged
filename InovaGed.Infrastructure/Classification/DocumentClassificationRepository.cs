using System.Data;
using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentClassificationRepository : IDocumentClassificationRepository
{
    private readonly IDbConnectionFactory _db;

    public DocumentClassificationRepository(IDbConnectionFactory db) => _db = db;

    public async Task UpsertClassificationAsync(Guid tenantId, Guid documentId, Guid documentVersionId,
        Guid? documentTypeId, decimal? confidence, string method, string? summary, Guid? classifiedBy, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO ged.document_classification
  (tenant_id, document_id, document_version_id, document_type_id, confidence, method, summary, classified_by, classified_at, reg_status)
VALUES
  (@tenantId, @documentId, @documentVersionId, @documentTypeId, @confidence, @method, @summary, @classifiedBy, now(), 'A')
ON CONFLICT (document_id) DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  document_version_id = EXCLUDED.document_version_id,
  document_type_id = EXCLUDED.document_type_id,
  confidence = EXCLUDED.confidence,
  method = EXCLUDED.method,
  summary = EXCLUDED.summary,
  classified_by = EXCLUDED.classified_by,
  classified_at = now(),
  reg_status = 'A'
WHERE ged.document_classification.tenant_id = EXCLUDED.tenant_id;";

        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            tenantId,
            documentId,
            documentVersionId,
            documentTypeId,
            confidence,
            method,
            summary,
            classifiedBy
        }, cancellationToken: ct));
    }

    public async Task UpsertTagsAsync(Guid tenantId, Guid documentId, IReadOnlyList<string> tags, string method, Guid? assignedBy, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await UpsertTagsCoreAsync(conn, null, tenantId, documentId, tags, method, assignedBy, ct);
    }

    public async Task UpsertMetadataAsync(Guid tenantId, Guid documentId, IReadOnlyDictionary<string, (string Value, decimal? Confidence)> metadata, string method, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await UpsertMetadataCoreAsync(conn, null, tenantId, documentId, metadata, method, ct);
    }

    public async Task ReplaceTagsAsync(Guid tenantId, Guid documentId, IReadOnlyList<string> tags, string method, Guid? assignedBy, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string del = @"DELETE FROM ged.document_tag WHERE tenant_id=@tenantId AND document_id=@documentId;";
        await conn.ExecuteAsync(new CommandDefinition(del, new { tenantId, documentId }, tx, cancellationToken: ct));

        await UpsertTagsCoreAsync(conn, tx, tenantId, documentId, tags, method, assignedBy, ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReplaceMetadataAsync(Guid tenantId, Guid documentId, IReadOnlyDictionary<string, (string Value, decimal? Confidence)> metadata, string method, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string del = @"DELETE FROM ged.document_metadata WHERE tenant_id=@tenantId AND document_id=@documentId;";
        await conn.ExecuteAsync(new CommandDefinition(del, new { tenantId, documentId }, tx, cancellationToken: ct));

        await UpsertMetadataCoreAsync(conn, tx, tenantId, documentId, metadata, method, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SetSuggestionAsync(Guid tenantId, Guid documentId, Guid? suggestedTypeId, decimal? suggestedConfidence, string? suggestedSummary, DateTimeOffset? suggestedAt, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.document_classification
SET suggested_type_id = @suggestedTypeId,
    suggested_confidence = @suggestedConfidence,
    suggested_summary = @suggestedSummary,
    suggested_at = @suggestedAt
WHERE tenant_id = @tenantId AND document_id = @documentId;";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { tenantId, documentId, suggestedTypeId, suggestedConfidence, suggestedSummary, suggestedAt },
            cancellationToken: ct));
    }

    public async Task<bool> HasClassificationAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
SELECT EXISTS(
  SELECT 1 FROM ged.document_classification c
  WHERE c.tenant_id=@tenantId AND c.document_id=@documentId AND c.document_type_id IS NOT NULL
);";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
    }

    public async Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(1)
FROM ged.document d
LEFT JOIN ged.document_classification c
  ON c.tenant_id=d.tenant_id AND c.document_id=d.id
WHERE d.tenant_id=@tenantId
  AND (@folderId IS NULL OR d.folder_id=@folderId)
  AND c.document_type_id IS NULL;";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));
    }

    private static async Task UpsertTagsCoreAsync(IDbConnection conn, IDbTransaction? tx, Guid tenantId, Guid documentId, IReadOnlyList<string> tags, string method, Guid? assignedBy, CancellationToken ct)
    {
        if (tags.Count == 0) return;

        const string ensureTagSql = @"
INSERT INTO ged.tag (id, tenant_id, name, reg_date, reg_status)
VALUES (gen_random_uuid(), @tenantId, @name, now(), 'A')
ON CONFLICT (tenant_id, name) DO UPDATE SET reg_status='A'
RETURNING id;";

        const string linkSql = @"
INSERT INTO ged.document_tag (tenant_id, document_id, tag_id, method, assigned_by, assigned_at)
VALUES (@tenantId, @documentId, @tagId, @method, @assignedBy, now())
ON CONFLICT (document_id, tag_id) DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  method = EXCLUDED.method,
  assigned_by = EXCLUDED.assigned_by,
  assigned_at = now()
WHERE ged.document_tag.tenant_id = EXCLUDED.tenant_id;";

        foreach (var tag in tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var tagId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(ensureTagSql, new { tenantId, name = tag }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(linkSql, new { tenantId, documentId, tagId, method, assignedBy }, tx, cancellationToken: ct));
        }
    }

    private static async Task UpsertMetadataCoreAsync(IDbConnection conn, IDbTransaction? tx, Guid tenantId, Guid documentId, IReadOnlyDictionary<string, (string Value, decimal? Confidence)> metadata, string method, CancellationToken ct)
    {
        if (metadata.Count == 0) return;

        const string sql = @"
INSERT INTO ged.document_metadata (tenant_id, document_id, key, value, confidence, method, extracted_at)
VALUES (@tenantId, @documentId, @key, @value, @confidence, @method, now())
ON CONFLICT (document_id, key) DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  value = EXCLUDED.value,
  confidence = EXCLUDED.confidence,
  method = EXCLUDED.method,
  extracted_at = now()
WHERE ged.document_metadata.tenant_id = EXCLUDED.tenant_id;";

        foreach (var kv in metadata.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
        {
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenantId,
                documentId,
                key = kv.Key.Trim(),
                value = kv.Value.Value,
                confidence = kv.Value.Confidence,
                method
            }, tx, cancellationToken: ct));
        }
    }
}
