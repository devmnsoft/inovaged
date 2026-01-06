using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentClassificationQueries : IDocumentClassificationQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentClassificationQueries> _logger;

    public DocumentClassificationQueries(
        IDbConnectionFactory db,
        ILogger<DocumentClassificationQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<DocumentClassificationViewDto?> GetAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken ct)
        => GetViewAsync(tenantId, documentId, ct);

    public async Task<DocumentClassificationViewDto?> GetViewAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken ct)
    {
        const string headSql = @"
SELECT
  d.id                         AS ""DocumentId"",
  d.tenant_id                  AS ""TenantId"",
  COALESCE(dv.id, '00000000-0000-0000-0000-000000000000'::uuid) AS ""DocumentVersionId"",

  dc.document_type_id          AS ""DocumentTypeId"",
  dt.name                      AS ""DocumentTypeName"",
  dc.confidence                AS ""Confidence"",
  COALESCE(dc.method,'RULES')  AS ""Method"",
  dc.summary                   AS ""Summary"",
  COALESCE(dc.classified_at, d.created_at) AS ""ClassifiedAt"",

  dc.suggested_type_id         AS ""SuggestedTypeId"",
  dts.name                     AS ""SuggestedTypeName"",
  dc.suggested_confidence      AS ""SuggestedConfidence"",
  dc.suggested_summary         AS ""SuggestedSummary"",
  dc.suggested_at              AS ""SuggestedAt""
FROM ged.document d
LEFT JOIN LATERAL (
  SELECT id
  FROM ged.document_versions
  WHERE tenant_id = d.tenant_id
    AND document_id = d.id
  ORDER BY created_at DESC
  LIMIT 1
) dv ON true
LEFT JOIN ged.document_classification dc
  ON dc.document_id = d.id
 AND dc.tenant_id = d.tenant_id
 AND dc.reg_status = 'A'
LEFT JOIN ged.document_type dt
  ON dt.id = dc.document_type_id
 AND dt.tenant_id = d.tenant_id
LEFT JOIN ged.document_type dts
  ON dts.id = dc.suggested_type_id
 AND dts.tenant_id = d.tenant_id
WHERE d.tenant_id = @TenantId
  AND d.id = @DocumentId
  AND d.status <> 'DELETED'
LIMIT 1;
";

        try
        {
            _logger.LogDebug("GetViewAsync | Tenant={TenantId} Document={DocumentId}", tenantId, documentId);

            await using var con = await _db.OpenAsync(ct);

            var dto = await con.QueryFirstOrDefaultAsync<DocumentClassificationViewDto>(
                new CommandDefinition(
                    headSql,
                    new { TenantId = tenantId, DocumentId = documentId },
                    cancellationToken: ct));

            if (dto is null) return null;

            // TAGS
            const string tagsSql = @"
SELECT t.name
FROM ged.document_tag dt
JOIN ged.tag t
  ON t.id = dt.tag_id
 AND t.tenant_id = dt.tenant_id
WHERE dt.tenant_id = @TenantId
  AND dt.document_id = @DocumentId
ORDER BY lower(t.name);
";
            var tags = (await con.QueryAsync<string>(
                new CommandDefinition(
                    tagsSql,
                    new { TenantId = tenantId, DocumentId = documentId },
                    cancellationToken: ct)))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // METADATA
            const string metaSql = @"
SELECT key, value
FROM ged.document_metadata
WHERE tenant_id = @TenantId
  AND document_id = @DocumentId
ORDER BY lower(key);
";
            var meta = (await con.QueryAsync<(string key, string value)>(
                new CommandDefinition(
                    metaSql,
                    new { TenantId = tenantId, DocumentId = documentId },
                    cancellationToken: ct)))
                .Where(x => !string.IsNullOrWhiteSpace(x.key))
                .GroupBy(x => x.key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Last().value ?? "",
                    StringComparer.OrdinalIgnoreCase);

            dto.Tags.AddRange(tags);
            foreach (var kv in meta)
                dto.Metadata[kv.Key] = kv.Value;

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em GetViewAsync | Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            throw;
        }
    }

    public async Task<bool> HasClassificationAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
SELECT EXISTS (
  SELECT 1
  FROM ged.document_classification dc
  WHERE dc.tenant_id = @TenantId
    AND dc.document_id = @DocumentId
    AND dc.document_type_id IS NOT NULL
    AND dc.reg_status = 'A'
);
";
        try
        {
            await using var con = await _db.OpenAsync(ct);
            return await con.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { TenantId = tenantId, DocumentId = documentId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em HasClassificationAsync | Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            throw;
        }
    }

    public async Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(1)
FROM ged.document d
LEFT JOIN ged.document_classification dc
  ON dc.document_id = d.id
 AND dc.tenant_id = d.tenant_id
 AND dc.reg_status = 'A'
WHERE d.tenant_id = @TenantId
  AND d.status <> 'DELETED'
  AND (@FolderId IS NULL OR d.folder_id = @FolderId)
  AND dc.document_type_id IS NULL;
";
        try
        {
            await using var con = await _db.OpenAsync(ct);
            return await con.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, new { TenantId = tenantId, FolderId = folderId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em CountUnclassifiedAsync | Tenant={TenantId} Folder={FolderId}", tenantId, folderId);
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentTypeRowDto>> ListTypesAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT id AS ""Id"", name AS ""Name""
FROM ged.document_type
WHERE tenant_id = @TenantId
ORDER BY lower(name);
";
        try
        {
            await using var con = await _db.OpenAsync(ct);
            var rows = await con.QueryAsync<DocumentTypeRowDto>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));
            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em ListTypesAsync | Tenant={TenantId}", tenantId);
            throw;
        }
    }
}
