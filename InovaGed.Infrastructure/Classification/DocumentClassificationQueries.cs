using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentClassificationQueries : IDocumentClassificationQueries
{
    private readonly IDbConnectionFactory _db;

    public DocumentClassificationQueries(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<DocumentClassificationViewDto?> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
-- 1) classificação
SELECT
  c.document_id         AS DocumentId,
  c.tenant_id           AS TenantId,
  c.document_type_id    AS DocumentTypeId,
  dt.name               AS DocumentTypeName,
  c.confidence          AS Confidence,
  c.method              AS Method,
  c.summary             AS Summary,
  c.classified_at       AS ClassifiedAt,
  c.suggested_type_id   AS SuggestedTypeId,
  sdt.name              AS SuggestedTypeName,
  c.suggested_confidence AS SuggestedConfidence,
  c.suggested_summary   AS SuggestedSummary,
  c.suggested_at        AS SuggestedAt
FROM ged.document_classification c
LEFT JOIN ged.document_type dt
  ON dt.id = c.document_type_id
 AND dt.tenant_id = c.tenant_id
LEFT JOIN ged.document_type sdt
  ON sdt.id = c.suggested_type_id
 AND sdt.tenant_id = c.tenant_id
WHERE c.tenant_id = @tenantId
  AND c.document_id = @documentId
LIMIT 1;

-- 2) tags
SELECT t.name
FROM ged.document_tag x
JOIN ged.tag t ON t.id = x.tag_id AND t.tenant_id = x.tenant_id
WHERE x.tenant_id = @tenantId
  AND x.document_id = @documentId
ORDER BY t.name;

-- 3) metadata
SELECT key, value
FROM ged.document_metadata
WHERE tenant_id = @tenantId
  AND document_id = @documentId
ORDER BY key;
";

        var conn = await _db.OpenAsync(ct);
        using var multi = await conn.QueryMultipleAsync(
            new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

        var cls = await multi.ReadSingleOrDefaultAsync<DocumentClassificationViewDto?>();
        if (cls is null) return null;

        var tags = (await multi.ReadAsync<string>()).ToList();
        var metaRows = await multi.ReadAsync<(string key, string value)>();

        return new DocumentClassificationViewDto
        {
            DocumentId = cls.DocumentId,
            TenantId = cls.TenantId,
            DocumentTypeId = cls.DocumentTypeId,
            DocumentTypeName = cls.DocumentTypeName,
            Confidence = cls.Confidence,
            Method = string.IsNullOrWhiteSpace(cls.Method) ? "RULES" : cls.Method,
            Summary = cls.Summary,
            ClassifiedAt = cls.ClassifiedAt,

            SuggestedTypeId = cls.SuggestedTypeId,
            SuggestedTypeName = cls.SuggestedTypeName,
            SuggestedConfidence = cls.SuggestedConfidence,
            SuggestedSummary = cls.SuggestedSummary,
            SuggestedAt = cls.SuggestedAt,

            Tags = tags,
            Metadata = metaRows.ToDictionary(x => x.key, x => x.value)
        };
    }

    // ✅ Novo: usado para travar workflow
    public async Task<bool> HasClassificationAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
SELECT EXISTS(
    SELECT 1
    FROM ged.document_classification c
    WHERE c.tenant_id = @tenantId
      AND c.document_id = @documentId
      AND c.document_type_id IS NOT NULL
);";
        var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
    }

    // ✅ Novo: KPI “Documentos não classificados”
    public async Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(1)
FROM ged.document d
LEFT JOIN ged.document_classification c
  ON c.tenant_id = d.tenant_id
 AND c.document_id = d.id
WHERE d.tenant_id = @tenantId
  AND (@folderId IS NULL OR d.folder_id = @folderId)
  AND c.document_type_id IS NULL;";
        var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));
    }
}
