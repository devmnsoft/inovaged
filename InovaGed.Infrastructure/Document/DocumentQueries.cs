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
        try
        {
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
  AND (@q IS NULL OR @q = '' OR
       d.title ILIKE ('%'||@q||'%') OR
       cv.file_name ILIKE ('%'||@q||'%') OR
       dt.name ILIKE ('%'||@q||'%'))
ORDER BY d.created_at DESC;";

            using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<DocumentRowDto>(
                new CommandDefinition(sql, new { tenantId, folderId, q }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar documentos. Tenant={TenantId}, Folder={FolderId}", tenantId, folderId);
            return Array.Empty<DocumentRowDto>();
        }
    }

    public async Task<DocumentDetailsDto?> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
    d.id                          AS ""Id"",
    d.folder_id                   AS ""FolderId"",
    d.code                        AS ""Code"",
    d.title                       AS ""Title"",
    d.description                 AS ""Description"",
    d.current_version_id          AS ""CurrentVersionId"",
    COALESCE(cv.version_number,0) AS ""CurrentVersion"",
    (d.visibility = 'CONFIDENTIAL'::ged.document_visibility_enum) AS ""IsConfidential"",
    d.created_at                  AS ""CreatedAt"",
    d.created_by                  AS ""CreatedBy""
FROM ged.document d
LEFT JOIN ged.document_version cv
       ON cv.id = d.current_version_id AND cv.tenant_id = d.tenant_id
WHERE d.tenant_id = @tenantId
  AND d.id = @documentId;";

            using var conn = await _db.OpenAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<DocumentDetailsDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar detalhes do documento. Tenant={TenantId}, Doc={DocId}", tenantId, documentId);
            return null;
        }
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
            SELECT
                v.id              AS ""Id"",
                v.version_number  AS ""VersionNumber"",
                v.file_name       AS ""FileName"",
                v.file_size_bytes AS ""SizeBytes"",
                v.content_type    AS ""ContentType"",
                v.created_at      AS ""CreatedAt"",
                v.created_by      AS ""CreatedBy""
            FROM ged.document_version v
            WHERE v.tenant_id = @tenantId
              AND v.document_id = @documentId
            ORDER BY v.version_number DESC;";

            using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<DocumentVersionDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar versões. Tenant={TenantId}, Doc={DocId}", tenantId, documentId);
            return Array.Empty<DocumentVersionDto>();
        }
    }

    public async Task<DocumentVersionDownloadDto?> GetVersionForDownloadAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
    v.id           AS ""Id"",
    v.document_id  AS ""DocumentId"",
    v.version_number AS ""VersionNumber"",
    v.file_name    AS ""FileName"",
    v.content_type AS ""ContentType"",
    v.storage_path AS ""StoragePath""
FROM ged.document_version v
WHERE v.tenant_id = @tenantId
  AND v.id = @versionId;";

            using var conn = await _db.OpenAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<DocumentVersionDownloadDto>(
                new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar versão para download. Tenant={TenantId}, VersionId={VersionId}", tenantId, versionId);
            return null;
        }
    }
}
