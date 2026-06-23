using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Domain.Documents;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
        var isDocumentIncompleteExpr = (schema.HasIsDocumentIncomplete, schema.HasPartialStatus) switch
        {
            (true, true) => "COALESCE(cv.is_document_incomplete,false) OR upper(COALESCE(cv.partial_status,'')) = 'INCOMPLETE'",
            (true, false) => "COALESCE(cv.is_document_incomplete,false)",
            (false, true) => "upper(COALESCE(cv.partial_status,'')) = 'INCOMPLETE'",
            _ => "false"
        };
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
        var partialPartsCountExpr = schema.HasDocumentPartialPartTable
            ? "(SELECT count(*)::int FROM ged.document_partial_part pp WHERE pp.tenant_id=d.tenant_id AND pp.document_id=d.id AND pp.reg_status='A')"
            : "0";
        var documentSearchJoin = BuildDocumentSearchJoin(schema, "d", "cv");
        var ocrTextExpr = schema.HasDocumentSearchOcrText ? "ds.ocr_text" : "NULL::text";
        var ocrStatusExpr = $"CASE WHEN oj.status IS NOT NULL THEN upper(oj.status::text) WHEN NULLIF(btrim(COALESCE({ocrTextExpr},'')), '') IS NOT NULL THEN 'COMPLETED' ELSE 'NONE' END";
        var hasOcrTextExpr = $"(NULLIF(btrim(COALESCE({ocrTextExpr},'')),'') IS NOT NULL)";
        var partialOcrJoin = BuildPartialOcrSummaryJoin(schema, "d", "cv");
        var partialPartsWithOcrExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? "COALESCE(pos.parts_with_ocr,0)" : "0";
        var partialPartsWithoutOcrExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? "GREATEST(COALESCE(pos.total_parts,0)-COALESCE(pos.parts_with_ocr,0),0)" : "0";
        var hasAnyPartialOcrExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? "COALESCE(pos.parts_with_ocr,0) > 0" : "false";
        var hasAllPartialOcrExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? "COALESCE(pos.total_parts,0) > 0 AND COALESCE(pos.parts_with_ocr,0) = COALESCE(pos.total_parts,0)" : "false";
        var ocrSummaryTextExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? $"CASE WHEN ({isPartialDocumentExpr}) OR COALESCE(pos.total_parts,0) > 0 THEN CASE WHEN upper(COALESCE({partialStatusExpr},'')) = 'CONSOLIDATED' AND ({ocrStatusExpr} = 'COMPLETED' AND {hasOcrTextExpr}) THEN 'OCR consolidado' WHEN COALESCE(pos.total_parts,0)=0 THEN '' WHEN COALESCE(pos.parts_with_ocr,0)=0 THEN 'Sem OCR nas partes' WHEN COALESCE(pos.parts_with_ocr,0)=COALESCE(pos.total_parts,0) THEN 'OCR disponível nas partes' ELSE 'OCR parcial ' || COALESCE(pos.parts_with_ocr,0)::text || '/' || COALESCE(pos.total_parts,0)::text END ELSE '' END" : "''";
        var ocrSummaryCssExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? $"CASE WHEN ({isPartialDocumentExpr}) OR COALESCE(pos.total_parts,0) > 0 THEN CASE WHEN upper(COALESCE({partialStatusExpr},'')) = 'CONSOLIDATED' AND ({ocrStatusExpr} = 'COMPLETED' AND {hasOcrTextExpr}) THEN 'bg-success' WHEN COALESCE(pos.total_parts,0)=0 THEN '' WHEN COALESCE(pos.parts_with_ocr,0)=0 THEN 'bg-secondary' WHEN COALESCE(pos.parts_with_ocr,0)=COALESCE(pos.total_parts,0) THEN 'bg-success' ELSE 'bg-warning text-dark' END ELSE '' END" : "''";
        var normalizedQ = NormalizeSearchTerm(q);
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
    {{ocrStatusExpr}} AS "OcrStatus",
    oj.finished_at                 AS "OcrFinishedAt",
    {{hasOcrTextExpr}} AS "HasOcrText",
    ({{ocrStatusExpr}} = 'COMPLETED' AND {{hasOcrTextExpr}}) AS "IsOcrAvailable",
    {{partialPartsWithOcrExpr}} AS "PartialPartsWithOcrCount",
    {{partialPartsWithoutOcrExpr}} AS "PartialPartsWithoutOcrCount",
    {{hasAnyPartialOcrExpr}} AS "HasAnyPartialOcr",
    {{hasAllPartialOcrExpr}} AS "HasAllPartialOcr",
    {{ocrSummaryTextExpr}} AS "OcrSummaryText",
    {{ocrSummaryCssExpr}} AS "OcrSummaryCss",
    dc.document_type_id            AS "ClassificationId",
    cdt.name                       AS "ClassificationLabel",
    NULL::text                     AS "ClassificationColor",
    NULL::text                     AS "ClassificationIcon",
    {{isPartialDocumentExpr}} AS "IsPartialDocument",
    {{partialGroupIdExpr}} AS "PartialGroupId",
    {{partialPartNumberExpr}} AS "PartialPartNumber",
    {{partialTotalPartsExpr}} AS "PartialTotalParts",
    {{partialStatusExpr}} AS "PartialStatus",
    {{isDocumentIncompleteExpr}} AS "IsDocumentIncomplete",
    {{partNumberExpr}}                 AS "PartNumber",
    {{totalPartsExpr}}                 AS "TotalParts",
    {{consolidatedVersionExpr}}     AS "ConsolidatedVersionId",
    {{partialPartsCountExpr}} AS "PartialPartsCount",
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
    ORDER BY COALESCE(j.finished_at, j.requested_at) DESC NULLS LAST
    LIMIT 1
) oj ON true
{{documentSearchJoin}}
{{partialOcrJoin}}
LEFT JOIN LATERAL (
    SELECT x.document_type_id
    FROM ged.document_classification x
    WHERE x.tenant_id = d.tenant_id
      AND x.document_id = d.id
      AND x.reg_status = 'A'
    ORDER BY x.classified_at DESC NULLS LAST, x.created_at DESC NULLS LAST
    LIMIT 1
) dc ON true
LEFT JOIN ged.document_type cdt
       ON cdt.tenant_id = d.tenant_id
      AND cdt.id = dc.document_type_id
WHERE d.tenant_id = @tenantId
  AND (
        (@folderId IS NULL AND d.folder_id IS NULL)
        OR
        (@folderId IS NOT NULL AND d.folder_id = @folderId)
      )
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND coalesce(d.reg_status, 'A') = 'A'
  AND (@q IS NULL OR @q = '' OR
       lower(coalesce(d.title, '')) LIKE ('%'||@q||'%') OR
       lower(coalesce(cv.file_name, '')) LIKE ('%'||@q||'%') OR
       lower(coalesce(dt.name, '')) LIKE ('%'||@q||'%') OR
       lower(coalesce(cdt.name, '')) LIKE ('%'||@q||'%') OR
       lower(coalesce(d.description, '')) LIKE ('%'||@q||'%') OR
       lower(coalesce(d.code, '')) LIKE ('%'||@q||'%') OR
       lower(coalesce({{ocrTextExpr}}, '')) LIKE ('%'||@q||'%'))
ORDER BY {{uploadedAtExpr}} DESC;
""";

        try
        {
            var rows = await conn.QueryAsync<DocumentRowDto>(
                new CommandDefinition(sql, new { tenantId, folderId, q = normalizedQ }, cancellationToken: ct));

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

    private static string? NormalizeSearchTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formD = value.Trim().Normalize(NormalizationForm.FormD);
        var withoutAccents = new string(formD.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);
        var collapsed = Regex.Replace(withoutAccents, @"\s+", " ");
        return string.IsNullOrWhiteSpace(collapsed) ? null : collapsed.ToLowerInvariant();
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
  AND coalesce(d.reg_status, 'A') = 'A';
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
        var isDocumentIncompleteExpr = (schema.HasIsDocumentIncomplete, schema.HasPartialStatus) switch
        {
            (true, true) => "COALESCE(v.is_document_incomplete,false) OR upper(COALESCE(v.partial_status,'')) = 'INCOMPLETE'",
            (true, false) => "COALESCE(v.is_document_incomplete,false)",
            (false, true) => "upper(COALESCE(v.partial_status,'')) = 'INCOMPLETE'",
            _ => "false"
        };
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
        var partialPartsCountExpr = schema.HasDocumentPartialPartTable
            ? "(SELECT count(*)::int FROM ged.document_partial_part pp WHERE pp.tenant_id=v.tenant_id AND pp.partial_group_id=v.partial_group_id AND pp.reg_status='A')"
            : "0";
        var documentSearchJoin = BuildDocumentSearchJoin(schema, "d", "v");
        var ocrTextExpr = schema.HasDocumentSearchOcrText ? "ds.ocr_text" : "NULL::text";
        var ocrStatusExpr = $"CASE WHEN oj.status IS NOT NULL THEN upper(oj.status::text) WHEN NULLIF(btrim(COALESCE({ocrTextExpr},'')), '') IS NOT NULL THEN 'COMPLETED' ELSE 'NONE' END";
        var hasOcrTextExpr = $"(NULLIF(btrim(COALESCE({ocrTextExpr},'')),'') IS NOT NULL)";
        var partialOcrJoin = BuildPartialOcrSummaryJoin(schema, "d", "v");
        var partialPartsWithOcrExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? "COALESCE(pos.parts_with_ocr,0)" : "0";
        var partialPartsWithoutOcrExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? "GREATEST(COALESCE(pos.total_parts,0)-COALESCE(pos.parts_with_ocr,0),0)" : "0";
        var hasAnyPartialOcrExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? "COALESCE(pos.parts_with_ocr,0) > 0" : "false";
        var hasAllPartialOcrExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? "COALESCE(pos.total_parts,0) > 0 AND COALESCE(pos.parts_with_ocr,0) = COALESCE(pos.total_parts,0)" : "false";
        var ocrSummaryTextExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? $"CASE WHEN ({isPartialDocumentExpr}) OR COALESCE(pos.total_parts,0) > 0 THEN CASE WHEN upper(COALESCE({partialStatusExpr},'')) = 'CONSOLIDATED' AND ({ocrStatusExpr} = 'COMPLETED' AND {hasOcrTextExpr}) THEN 'OCR consolidado' WHEN COALESCE(pos.total_parts,0)=0 THEN '' WHEN COALESCE(pos.parts_with_ocr,0)=0 THEN 'Sem OCR nas partes' WHEN COALESCE(pos.parts_with_ocr,0)=COALESCE(pos.total_parts,0) THEN 'OCR disponível nas partes' ELSE 'OCR parcial ' || COALESCE(pos.parts_with_ocr,0)::text || '/' || COALESCE(pos.total_parts,0)::text END ELSE '' END" : "''";
        var ocrSummaryCssExpr = schema.HasDocumentPartialPartTable && schema.HasDocumentSearchOcrText ? $"CASE WHEN ({isPartialDocumentExpr}) OR COALESCE(pos.total_parts,0) > 0 THEN CASE WHEN upper(COALESCE({partialStatusExpr},'')) = 'CONSOLIDATED' AND ({ocrStatusExpr} = 'COMPLETED' AND {hasOcrTextExpr}) THEN 'bg-success' WHEN COALESCE(pos.total_parts,0)=0 THEN '' WHEN COALESCE(pos.parts_with_ocr,0)=0 THEN 'bg-secondary' WHEN COALESCE(pos.parts_with_ocr,0)=COALESCE(pos.total_parts,0) THEN 'bg-success' ELSE 'bg-warning text-dark' END ELSE '' END" : "''";
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
    {{hasOcrTextExpr}} AS "HasOcrText",
    ({{ocrStatusExpr}} = 'COMPLETED' AND {{hasOcrTextExpr}}) AS "IsOcrAvailable",
    {{partialPartsWithOcrExpr}} AS "PartialPartsWithOcrCount",
    {{partialPartsWithoutOcrExpr}} AS "PartialPartsWithoutOcrCount",
    {{hasAnyPartialOcrExpr}} AS "HasAnyPartialOcr",
    {{hasAllPartialOcrExpr}} AS "HasAllPartialOcr",
    {{ocrSummaryTextExpr}} AS "OcrSummaryText",
    {{ocrSummaryCssExpr}} AS "OcrSummaryCss",
    {{isPartialDocumentExpr}} AS "IsPartialDocument",
    {{partialGroupIdExpr}} AS "PartialGroupId",
    {{partialPartNumberExpr}} AS "PartialPartNumber",
    {{partialTotalPartsExpr}} AS "PartialTotalParts",
    {{partialStatusExpr}} AS "PartialStatus",
    {{isDocumentIncompleteExpr}} AS "IsDocumentIncomplete",
    {{partNumberExpr}} AS "PartNumber",
    {{totalPartsExpr}} AS "TotalParts",
    {{consolidatedVersionExpr}} AS "ConsolidatedVersionId",
    {{partialPartsCountExpr}} AS "PartialPartsCount",

    {{ocrStatusExpr}} AS "OcrStatus",
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
    ORDER BY COALESCE(j.finished_at, j.requested_at) DESC NULLS LAST
    LIMIT 1
) oj ON true
{{documentSearchJoin}}
{{partialOcrJoin}}
WHERE v.tenant_id = @tenantId
  AND v.document_id = @documentId
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND coalesce(d.reg_status, 'A') = 'A'
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
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='created_at_utc') AS "HasDocumentCreatedAtUtc",
    (to_regclass('ged.document_partial_part') IS NOT NULL) AS "HasDocumentPartialPartTable",
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='ocr_text') AS "HasDocumentSearchOcrText",
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='document_id') AS "HasDocumentSearchDocumentId",
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='version_id') AS "HasDocumentSearchVersionId",
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='document_version_id') AS "HasDocumentSearchDocumentVersionId"
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


    private static string BuildPartialOcrSummaryJoin(DocumentVersionSchema schema, string documentAlias, string versionAlias)
    {
        if (!schema.HasDocumentPartialPartTable || !schema.HasDocumentSearchOcrText || !schema.HasPartialGroupId)
        {
            return "LEFT JOIN LATERAL (SELECT 0::int AS total_parts, 0::int AS parts_with_ocr) pos ON false";
        }

        var searchVersionPredicate = BuildDocumentSearchVersionPredicate(schema, "ds", "pp.version_id");
        var searchJoin = searchVersionPredicate is null
            ? "LEFT JOIN (SELECT NULL::uuid AS tenant_id, NULL::text AS ocr_text WHERE false) ds ON false"
            : $"LEFT JOIN ged.document_search ds ON ds.tenant_id = pp.tenant_id AND {searchVersionPredicate}";

        return $@"LEFT JOIN LATERAL (
    SELECT count(*)::int AS total_parts,
           count(*) FILTER (
               WHERE NULLIF(btrim(COALESCE(ds.ocr_text,'')), '') IS NOT NULL
                 AND upper(COALESCE(oj.status::text,'')) = 'COMPLETED'
           )::int AS parts_with_ocr
    FROM ged.document_partial_part pp
    LEFT JOIN LATERAL (
        SELECT j.*
        FROM ged.ocr_job j
        WHERE j.tenant_id = pp.tenant_id
          AND j.document_version_id = pp.version_id
        ORDER BY COALESCE(j.finished_at, j.requested_at) DESC NULLS LAST
        LIMIT 1
    ) oj ON true
    {searchJoin}
    WHERE pp.tenant_id = {documentAlias}.tenant_id
      AND pp.partial_group_id = {versionAlias}.partial_group_id
      AND COALESCE(pp.reg_status, 'A') = 'A'
) pos ON true";
    }

    private static string? BuildDocumentSearchVersionPredicate(DocumentVersionSchema schema, string searchAlias, string versionExpression)
    {
        var predicates = new List<string>();
        if (schema.HasDocumentSearchVersionId)
        {
            predicates.Add($"{searchAlias}.version_id = {versionExpression}");
        }
        if (schema.HasDocumentSearchDocumentVersionId)
        {
            predicates.Add($"{searchAlias}.document_version_id = {versionExpression}");
        }

        return predicates.Count == 0 ? null : $"({string.Join(" OR ", predicates)})";
    }

    private static string BuildDocumentSearchJoin(DocumentVersionSchema schema, string documentAlias, string versionAlias)
    {
        if (!schema.HasDocumentSearchOcrText)
        {
            return "LEFT JOIN (SELECT NULL::uuid AS tenant_id, NULL::text AS ocr_text WHERE false) ds ON false";
        }

        var predicates = new List<string>();
        if (schema.HasDocumentSearchDocumentId)
        {
            predicates.Add($"ds.document_id = {documentAlias}.id");
        }

        var versionPredicates = new List<string>();
        if (schema.HasDocumentSearchVersionId)
        {
            versionPredicates.Add($"ds.version_id = {versionAlias}.id");
        }
        if (schema.HasDocumentSearchDocumentVersionId)
        {
            versionPredicates.Add($"ds.document_version_id = {versionAlias}.id");
        }
        if (versionPredicates.Count > 0)
        {
            predicates.Add($"({string.Join(" OR ", versionPredicates)})");
        }

        var entityPredicate = predicates.Count == 0 ? "true" : string.Join(" AND ", predicates);
        return $"LEFT JOIN ged.document_search ds ON ds.tenant_id = {documentAlias}.tenant_id AND {entityPredicate}";
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
        public bool HasDocumentPartialPartTable { get; set; }
        public bool HasDocumentSearchOcrText { get; set; }
        public bool HasDocumentSearchDocumentId { get; set; }
        public bool HasDocumentSearchVersionId { get; set; }
        public bool HasDocumentSearchDocumentVersionId { get; set; }

        public bool HasPartialDocumentMetadata =>
            HasUploadedAtUtc &&
            HasIsPartialDocument &&
            HasPartialGroupId &&
            HasPartialPartNumber &&
            HasPartialTotalParts &&
            HasPartialStatus &&
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
  AND coalesce(d.reg_status, 'A') = 'A';
""";

        await using var conn = await _db.OpenAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<DocumentVersionDownloadDto>(
            new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
    }
}