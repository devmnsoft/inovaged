using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Domain.Documents;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Documents;

public sealed class DocumentQueries : IDocumentQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentQueries> _logger;

    public DocumentQueries(IDbConnectionFactory db, ILogger<DocumentQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentRowDto>> ListAsync(Guid tenantId, Guid? folderId, string? q, CancellationToken ct)
    {
        _logger.LogInformation("DocumentQueries.ListAsync START | Tenant={TenantId} Folder={FolderId} Q={Q}",
            tenantId, folderId, q);

        const string sql = @"
SELECT
    d.id                           AS ""Id"",
    d.title                        AS ""Title"",
    COALESCE(dt.name,'-')          AS ""TypeName"",
    cv.file_name                   AS ""FileName"",
    COALESCE(cv.file_size_bytes,0) AS ""SizeBytes"",
    d.created_at                   AS ""CreatedAt"",
    d.created_by                   AS ""CreatedBy"",
    (d.visibility = 'CONFIDENTIAL'::ged.document_visibility_enum) AS ""IsConfidential""
FROM ged.document d
LEFT JOIN ged.document_type dt
       ON dt.id = d.type_id AND dt.tenant_id = d.tenant_id
LEFT JOIN ged.document_version cv
       ON cv.id = d.current_version_id AND cv.tenant_id = d.tenant_id
WHERE d.tenant_id = @tenantId
  AND (
        (@folderId IS NULL AND d.folder_id IS NULL)
        OR
        (@folderId IS NOT NULL AND d.folder_id = @folderId)
      )
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND (@q IS NULL OR @q = '' OR
       d.title ILIKE ('%'||@q||'%') OR
       cv.file_name ILIKE ('%'||@q||'%') OR
       dt.name ILIKE ('%'||@q||'%'))
ORDER BY d.created_at DESC;";

        _logger.LogDebug("DocumentQueries.ListAsync SQL:\n{Sql}", sql);

        try
        {
            using var conn = await _db.OpenAsync(ct);

            var rows = await conn.QueryAsync<DocumentRowDto>(
                new CommandDefinition(sql, new { tenantId, folderId, q }, cancellationToken: ct));

            var list = rows.AsList();

            _logger.LogInformation("DocumentQueries.ListAsync SUCCESS | Count={Count}", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DocumentQueries.ListAsync ERROR | Tenant={TenantId} Folder={FolderId}", tenantId, folderId);
            throw;
        }
    }

    public async Task<DocumentDetailsDto?> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        _logger.LogInformation("DocumentQueries.GetAsync START | Tenant={TenantId} Doc={DocId}",
            tenantId, documentId);

        const string sql = @"
SELECT
    d.id                 AS ""Id"",
    d.tenant_id          AS ""TenantId"",
    d.folder_id          AS ""FolderId"",
    d.title              AS ""Title"",
    d.type_id            AS ""TypeId"",
    d.visibility         AS ""Visibility"",
    d.created_at         AS ""CreatedAt"",
    d.created_by         AS ""CreatedBy"",
    d.updated_at         AS ""UpdatedAt"",
    d.updated_by         AS ""UpdatedBy"",
    d.status             AS ""Status"",
    d.current_version_id AS ""CurrentVersionId""
FROM ged.document d
WHERE d.tenant_id = @tenantId
  AND d.id = @documentId;";

        _logger.LogDebug("DocumentQueries.GetAsync SQL:\n{Sql}", sql);

        try
        {
            using var conn = await _db.OpenAsync(ct);

            var dto = await conn.QueryFirstOrDefaultAsync<DocumentDetailsDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            if (dto is null)
                _logger.LogWarning("DocumentQueries.GetAsync NOT FOUND | Tenant={TenantId} Doc={DocId}", tenantId, documentId);
            else
                _logger.LogInformation("DocumentQueries.GetAsync SUCCESS | Doc={DocId}", documentId);

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DocumentQueries.GetAsync ERROR | Tenant={TenantId} Doc={DocId}", tenantId, documentId);
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        _logger.LogInformation("DocumentQueries.ListVersionsAsync START | Tenant={TenantId} Doc={DocId}",
            tenantId, documentId);

        // ✅ NÃO existe v.is_current no seu banco.
        // ✅ IsCurrent é calculado por v.id = d.current_version_id
        const string sql = @"
SELECT
    v.id              AS ""Id"",
    v.document_id     AS ""DocumentId"",
    v.file_name       AS ""FileName"",
    v.content_type    AS ""ContentType"",
    v.file_size_bytes AS ""SizeBytes"",
    v.storage_path    AS ""StoragePath"",
    v.created_at      AS ""CreatedAt"",
    v.created_by      AS ""CreatedBy"",
    (v.id = d.current_version_id) AS ""IsCurrent""
FROM ged.document_version v
JOIN ged.document d
  ON d.tenant_id = v.tenant_id
 AND d.id = v.document_id
WHERE v.tenant_id = @tenantId
  AND v.document_id = @documentId
ORDER BY v.created_at DESC;";

        _logger.LogWarning("DocumentQueries.ListVersionsAsync SQL EM USO:\n{Sql}", sql);

        try
        {
            using var conn = await _db.OpenAsync(ct);

            var rows = await conn.QueryAsync<DocumentVersionDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            var list = rows.AsList();

            _logger.LogInformation("DocumentQueries.ListVersionsAsync SUCCESS | Doc={DocId} Count={Count}",
                documentId, list.Count);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DocumentQueries.ListVersionsAsync ERROR | Tenant={TenantId} Doc={DocId}",
                tenantId, documentId);
            throw;
        }
    }

    public async Task<DocumentVersionDownloadDto?> GetVersionForDownloadAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        _logger.LogInformation("DocumentQueries.GetVersionForDownloadAsync START | Tenant={TenantId} Version={VersionId}",
            tenantId, versionId);

        const string sql = @"
SELECT
    v.id           AS ""VersionId"",
    v.document_id  AS ""DocumentId"",
    v.file_name    AS ""FileName"",
    v.content_type AS ""ContentType"",
    v.storage_path AS ""StoragePath""
FROM ged.document_version v
WHERE v.tenant_id = @tenantId
  AND v.id = @versionId;";

        _logger.LogDebug("DocumentQueries.GetVersionForDownloadAsync SQL:\n{Sql}", sql);

        try
        {
            using var conn = await _db.OpenAsync(ct);

            var dto = await conn.QuerySingleOrDefaultAsync<DocumentVersionDownloadDto>(
           new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));


            if (dto is null)
                _logger.LogWarning("DocumentQueries.GetVersionForDownloadAsync NOT FOUND | Tenant={TenantId} Version={VersionId}", tenantId, versionId);
            else
                _logger.LogInformation("DocumentQueries.GetVersionForDownloadAsync SUCCESS | Version={VersionId}", versionId);

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DocumentQueries.GetVersionForDownloadAsync ERROR | Tenant={TenantId} Version={VersionId}",
                tenantId, versionId);
            throw;
        }
    }
}
