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
       ON dt.id = d.type_id
      AND dt.tenant_id = d.tenant_id
LEFT JOIN ged.document_version cv
       ON cv.id = d.current_version_id
      AND cv.tenant_id = d.tenant_id
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

        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync<DocumentRowDto>(
            new CommandDefinition(sql, new { tenantId, folderId, q }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<DocumentDetailsDto?> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
SELECT
    d.id                 AS ""Id"",
    d.tenant_id          AS ""TenantId"",
    ''                   AS ""Code"",
    d.title              AS ""Title"",
    d.description        AS ""Description"",
    d.folder_id          AS ""FolderId"",
    d.type_id            AS ""TypeId"",
    NULL::uuid           AS ""ClassificationId"",
    d.status             AS ""Status"",
    d.visibility::text   AS ""Visibility"",
    d.current_version_id AS ""CurrentVersionId"",
    d.created_at         AS ""CreatedAt"",
    d.created_by         AS ""CreatedBy"",
    d.updated_at         AS ""UpdatedAt"",
    d.updated_by         AS ""UpdatedBy"",
    0                    AS ""CurrentVersion""
FROM ged.document d
WHERE d.tenant_id = @tenantId
  AND d.id = @documentId;";

        await using var conn = await _db.OpenAsync(ct);

        return await conn.QueryFirstOrDefaultAsync<DocumentDetailsDto>(
            new CommandDefinition(
                sql,
                new
                {
                    tenantId,
                    documentId
                },
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
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
    (v.id = d.current_version_id) AS ""IsCurrent"",

    oj.status::text   AS ""OcrStatus"",
    oj.id             AS ""OcrJobId"",
    oj.error_message  AS ""OcrErrorMessage"",
    oj.requested_at   AS ""OcrRequestedAt"",
    oj.started_at     AS ""OcrStartedAt"",
    oj.finished_at    AS ""OcrFinishedAt"",
    oj.invalidate_digital_signatures AS ""OcrInvalidateDigitalSignatures""
FROM ged.document_version v
JOIN ged.document d
  ON d.tenant_id = v.tenant_id
 AND d.id = v.document_id
LEFT JOIN LATERAL (
    SELECT j.*
    FROM ged.ocr_job j
    WHERE j.tenant_id = v.tenant_id
      AND j.document_version_id = v.id
    ORDER BY j.requested_at DESC
    LIMIT 1
) oj ON true
WHERE v.tenant_id = @tenantId
  AND v.document_id = @documentId
ORDER BY v.created_at DESC;";

        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync<DocumentVersionDto>(
            new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<DocumentVersionDownloadDto?> GetVersionForDownloadAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
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

        await using var conn = await _db.OpenAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<DocumentVersionDownloadDto>(
            new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
    }
}