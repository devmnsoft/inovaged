using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Domain.Documents;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Logging;
using Npgsql;

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
        await using var conn = await _db.OpenAsync(ct);
        var schema = await GetDocumentVersionSchemaAsync(conn, ct);

        var uploadedAtExpr = schema.HasUploadedAtUtc
            ? (schema.HasDocumentCreatedAtUtc
                ? "COALESCE(cv.uploaded_at_utc, cv.created_at, d.created_at_utc, d.created_at)"
                : "COALESCE(cv.uploaded_at_utc, cv.created_at, d.created_at)")
            : (schema.HasDocumentCreatedAtUtc
                ? "COALESCE(cv.created_at, d.created_at_utc, d.created_at)"
                : "COALESCE(cv.created_at, d.created_at)");
        var isPartialDocumentExpr = schema.HasIsPartialDocument ? "COALESCE(cv.is_partial_document,false)" : "false";
        var partialGroupIdExpr = schema.HasPartialGroupId ? "cv.partial_group_id" : "NULL::uuid";
        var partialPartNumberExpr = schema.HasPartialPartNumber ? "cv.partial_part_number" : "NULL::int";
        var partialTotalPartsExpr = schema.HasPartialTotalParts ? "cv.partial_total_parts" : "NULL::int";
        var partialStatusExpr = schema.HasPartialStatus ? "COALESCE(cv.partial_status, 'NOT_PARTIAL')" : "'NOT_PARTIAL'";
        var isDocumentIncompleteExpr = schema.HasIsDocumentIncomplete ? "COALESCE(cv.is_document_incomplete,false)" : "false";
        var partNumberExpr = (schema.HasPartNumber, schema.HasPartialPartNumber) switch
        {
            (true, true) => "COALESCE(cv.part_number, cv.partial_part_number)",
            (true, false) => "cv.part_number",
            (false, true) => "cv.partial_part_number",
            _ => "NULL::int"
        };
        var totalPartsExpr = (schema.HasTotalParts, schema.HasPartialTotalParts) switch
        {
            (true, true) => "COALESCE(cv.total_parts, cv.partial_total_parts)",
            (true, false) => "cv.total_parts",
            (false, true) => "cv.partial_total_parts",
            _ => "NULL::int"
        };
        var consolidatedVersionExpr = schema.HasConsolidatedVersionId ? "cv.consolidated_version_id" : "NULL::uuid";

        var sql = $$"""
SELECT
    d.id                           AS "Id",
    d.title                        AS "Title",
    COALESCE(dt.name,'-')          AS "TypeName",
    cv.file_name                   AS "FileName",
    d.current_version_id           AS "CurrentVersionId",
    COALESCE(cv.file_size_bytes,0) AS "SizeBytes",
    d.created_at                   AS "CreatedAt",
    {{uploadedAtExpr}} AS "UploadedAtUtc",
    d.created_by                   AS "CreatedBy",
    COALESCE(oj.status::text, 'NONE') AS "OcrStatus",
    oj.finished_at                 AS "OcrFinishedAt",
    (NULLIF(COALESCE(ds.ocr_text,''),'') IS NOT NULL) AS "HasOcrText",
    (upper(COALESCE(oj.status::text,'')) = 'COMPLETED' AND NULLIF(COALESCE(ds.ocr_text,''),'') IS NOT NULL) AS "IsOcrAvailable",
    {{isPartialDocumentExpr}} AS "IsPartialDocument",
    {{partialGroupIdExpr}} AS "PartialGroupId",
    {{partialPartNumberExpr}} AS "PartialPartNumber",
    {{partialTotalPartsExpr}} AS "PartialTotalParts",
    {{partialStatusExpr}} AS "PartialStatus",
    {{isDocumentIncompleteExpr}} AS "IsDocumentIncomplete",
    {{partNumberExpr}}                 AS "PartNumber",
    {{totalPartsExpr}}                 AS "TotalParts",
    {{consolidatedVersionExpr}}     AS "ConsolidatedVersionId",
    (d.visibility = 'CONFIDENTIAL'::ged.document_visibility_enum) AS "IsConfidential"
FROM ged.document d
LEFT JOIN ged.document_type dt
       ON dt.id = d.type_id
      AND dt.tenant_id = d.tenant_id
LEFT JOIN ged.document_version cv
       ON cv.id = d.current_version_id
      AND cv.tenant_id = d.tenant_id
LEFT JOIN LATERAL (
    SELECT j.*
    FROM ged.ocr_job j
    WHERE j.tenant_id = d.tenant_id
      AND j.document_version_id = cv.id
    ORDER BY j.requested_at DESC
    LIMIT 1
) oj ON true
LEFT JOIN ged.document_search ds
       ON ds.tenant_id = d.tenant_id
      AND ds.document_id = d.id
      AND ds.version_id = cv.id
WHERE d.tenant_id = @tenantId
  AND (
        (@folderId IS NULL AND d.folder_id IS NULL)
        OR
        (@folderId IS NOT NULL AND d.folder_id = @folderId)
      )
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND d.reg_status = 'A'::bpchar
  AND (@q IS NULL OR @q = '' OR
       d.title ILIKE ('%'||@q||'%') OR
       cv.file_name ILIKE ('%'||@q||'%') OR
       dt.name ILIKE ('%'||@q||'%'))
ORDER BY {{uploadedAtExpr}} DESC;
""";

        try
        {
            var rows = await conn.QueryAsync<DocumentRowDto>(
                new CommandDefinition(sql, new { tenantId, folderId, q }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (PostgresException ex) when (ex.SqlState is "42703" or "42P01")
        {
            _logger.LogError(ex,
                "Erro de schema ao listar documentos. Verifique migrations de document_version/uploaded_at_utc. Tenant={TenantId} Folder={FolderId}",
                tenantId,
                folderId);

            throw;
        }
    }

    public async Task<DocumentDetailsDto?> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = """
SELECT
    d.id                 AS "Id",
    d.tenant_id          AS "TenantId",
    ''                   AS "Code",
    d.title              AS "Title",
    d.description        AS "Description",
    d.folder_id          AS "FolderId",
    d.type_id            AS "TypeId",
    NULL::uuid           AS "ClassificationId",
    d.status             AS "Status",
    d.visibility::text   AS "Visibility",
    d.current_version_id AS "CurrentVersionId",
    d.created_at         AS "CreatedAt",
    d.created_by         AS "CreatedBy",
    d.updated_at         AS "UpdatedAt",
    d.updated_by         AS "UpdatedBy",
    0                    AS "CurrentVersion"
FROM ged.document d
WHERE d.tenant_id = @tenantId
  AND d.id = @documentId
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND d.reg_status = 'A'::bpchar;
""";

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
        await using var conn = await _db.OpenAsync(ct);
        var schema = await GetDocumentVersionSchemaAsync(conn, ct);

        var uploadedAtExpr = schema.HasUploadedAtUtc ? "COALESCE(v.uploaded_at_utc, v.created_at)" : "v.created_at";
        var isPartialDocumentExpr = schema.HasIsPartialDocument ? "COALESCE(v.is_partial_document,false)" : "false";
        var partialGroupIdExpr = schema.HasPartialGroupId ? "v.partial_group_id" : "NULL::uuid";
        var partialPartNumberExpr = schema.HasPartialPartNumber ? "v.partial_part_number" : "NULL::int";
        var partialTotalPartsExpr = schema.HasPartialTotalParts ? "v.partial_total_parts" : "NULL::int";
        var partialStatusExpr = schema.HasPartialStatus ? "COALESCE(v.partial_status, 'NOT_PARTIAL')" : "'NOT_PARTIAL'";
        var isDocumentIncompleteExpr = schema.HasIsDocumentIncomplete ? "COALESCE(v.is_document_incomplete,false)" : "false";
        var partNumberExpr = (schema.HasPartNumber, schema.HasPartialPartNumber) switch
        {
            (true, true) => "COALESCE(v.part_number, v.partial_part_number)",
            (true, false) => "v.part_number",
            (false, true) => "v.partial_part_number",
            _ => "NULL::int"
        };
        var totalPartsExpr = (schema.HasTotalParts, schema.HasPartialTotalParts) switch
        {
            (true, true) => "COALESCE(v.total_parts, v.partial_total_parts)",
            (true, false) => "v.total_parts",
            (false, true) => "v.partial_total_parts",
            _ => "NULL::int"
        };
        var consolidatedVersionExpr = schema.HasConsolidatedVersionId ? "v.consolidated_version_id" : "NULL::uuid";

        var sql = $$"""
SELECT
    v.id              AS "Id",
    v.document_id     AS "DocumentId",
    v.file_name       AS "FileName",
    v.content_type    AS "ContentType",
    v.file_size_bytes AS "SizeBytes",
    v.storage_path    AS "StoragePath",
    v.created_at      AS "CreatedAt",
    {{uploadedAtExpr}} AS "UploadedAtUtc",
    v.created_by      AS "CreatedBy",
    (v.id = d.current_version_id) AS "IsCurrent",
    (NULLIF(COALESCE(ds.ocr_text,''),'') IS NOT NULL) AS "HasOcrText",
    (upper(COALESCE(oj.status::text,'')) = 'COMPLETED' AND NULLIF(COALESCE(ds.ocr_text,''),'') IS NOT NULL) AS "IsOcrAvailable",
    {{isPartialDocumentExpr}} AS "IsPartialDocument",
    {{partialGroupIdExpr}} AS "PartialGroupId",
    {{partialPartNumberExpr}} AS "PartialPartNumber",
    {{partialTotalPartsExpr}} AS "PartialTotalParts",
    {{partialStatusExpr}} AS "PartialStatus",
    {{isDocumentIncompleteExpr}} AS "IsDocumentIncomplete",
    {{partNumberExpr}} AS "PartNumber",
    {{totalPartsExpr}} AS "TotalParts",
    {{consolidatedVersionExpr}} AS "ConsolidatedVersionId",

    oj.status::text   AS "OcrStatus",
    oj.id             AS "OcrJobId",
    oj.error_message  AS "OcrErrorMessage",
    oj.requested_at   AS "OcrRequestedAt",
    oj.started_at     AS "OcrStartedAt",
    oj.finished_at    AS "OcrFinishedAt",
    oj.invalidate_digital_signatures AS "OcrInvalidateDigitalSignatures"
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
LEFT JOIN ged.document_search ds
  ON ds.tenant_id = v.tenant_id
 AND ds.document_id = v.document_id
 AND ds.version_id = v.id
WHERE v.tenant_id = @tenantId
  AND v.document_id = @documentId
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND d.reg_status = 'A'::bpchar
ORDER BY v.created_at DESC;
""";

        try
        {
            var rows = await conn.QueryAsync<DocumentVersionDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (PostgresException ex) when (ex.SqlState is "42703" or "42P01")
        {
            _logger.LogError(ex,
                "Erro de schema ao listar versões de documento. Verifique migrations de document_version/uploaded_at_utc. Tenant={TenantId} DocumentId={DocumentId}",
                tenantId,
                documentId);

            throw;
        }
    }

    private async Task<DocumentVersionSchema> GetDocumentVersionSchemaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = """
SELECT
    COALESCE(bool_or(column_name = 'uploaded_at_utc'), false) AS "HasUploadedAtUtc",
    COALESCE(bool_or(column_name = 'is_partial_document'), false) AS "HasIsPartialDocument",
    COALESCE(bool_or(column_name = 'is_document_incomplete'), false) AS "HasIsDocumentIncomplete",
    COALESCE(bool_or(column_name = 'partial_group_id'), false) AS "HasPartialGroupId",
    COALESCE(bool_or(column_name = 'partial_part_number'), false) AS "HasPartialPartNumber",
    COALESCE(bool_or(column_name = 'partial_total_parts'), false) AS "HasPartialTotalParts",
    COALESCE(bool_or(column_name = 'partial_status'), false) AS "HasPartialStatus",
    COALESCE(bool_or(column_name = 'part_number'), false) AS "HasPartNumber",
    COALESCE(bool_or(column_name = 'total_parts'), false) AS "HasTotalParts",
    COALESCE(bool_or(column_name = 'consolidated_version_id'), false) AS "HasConsolidatedVersionId",
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='created_at_utc') AS "HasDocumentCreatedAtUtc"
FROM information_schema.columns
WHERE table_schema = 'ged'
  AND table_name = 'document_version'
  AND column_name IN (
      'uploaded_at_utc',
      'is_partial_document',
      'is_document_incomplete',
      'partial_group_id',
      'partial_part_number',
      'partial_total_parts',
      'partial_status',
      'part_number',
      'total_parts',
      'consolidated_version_id'
  );
""";

        var schema = await conn.QuerySingleAsync<DocumentVersionSchema>(
            new CommandDefinition(sql, cancellationToken: ct));

        if (!schema.HasPartialDocumentMetadata)
        {
            _logger.LogWarning(
                "Schema ged.document_version sem metadados de documento fracionado. Execute a migration 2026_06_ged_schema_consolidation.sql.");
        }

        return schema;
    }

    private sealed class DocumentVersionSchema
    {
        public bool HasUploadedAtUtc { get; set; }
        public bool HasIsPartialDocument { get; set; }
        public bool HasIsDocumentIncomplete { get; set; }
        public bool HasPartialGroupId { get; set; }
        public bool HasPartialPartNumber { get; set; }
        public bool HasPartialTotalParts { get; set; }
        public bool HasPartialStatus { get; set; }
        public bool HasDocumentCreatedAtUtc { get; set; }
        public bool HasPartNumber { get; set; }
        public bool HasTotalParts { get; set; }
        public bool HasConsolidatedVersionId { get; set; }

        public bool HasPartialDocumentMetadata =>
            HasUploadedAtUtc &&
            HasIsPartialDocument &&
            HasIsDocumentIncomplete &&
            HasPartialGroupId &&
            HasPartialPartNumber &&
            HasPartialTotalParts &&
            HasPartialStatus &&
            HasPartNumber &&
            HasTotalParts &&
            HasConsolidatedVersionId;
    }

    public async Task<DocumentVersionDownloadDto?> GetVersionForDownloadAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        const string sql = """
SELECT
    v.id           AS "VersionId",
    v.document_id  AS "DocumentId",
    v.file_name    AS "FileName",
    v.content_type AS "ContentType",
    v.storage_path AS "StoragePath"
FROM ged.document_version v
JOIN ged.document d
  ON d.tenant_id = v.tenant_id
 AND d.id = v.document_id
WHERE v.tenant_id = @tenantId
  AND v.id = @versionId
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND d.reg_status = 'A'::bpchar;
""";

        await using var conn = await _db.OpenAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<DocumentVersionDownloadDto>(
            new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
    }
}