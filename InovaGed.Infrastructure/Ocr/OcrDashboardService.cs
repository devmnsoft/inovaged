using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrDashboardService : IOcrDashboardService
{
    private readonly IDbConnectionFactory _db;
    private readonly IOptionsMonitor<OcrAutoScheduleOptions> _options;
    private readonly ILogger<OcrDashboardService> _logger;

    public OcrDashboardService(
        IDbConnectionFactory db,
        IOptionsMonitor<OcrAutoScheduleOptions> options,
        ILogger<OcrDashboardService> logger)
    {
        _db = db;
        _options = options;
        _logger = logger;
    }

    public async Task<OcrDashboardVm> GetDashboardAsync(Guid tenantId, OcrDashboardFilter filter, CancellationToken ct)
    {
        var items = await GetQueueAsync(tenantId, filter, ct);
        var options = _options.CurrentValue;
        var nextRun = OcrAutoScheduleClock.CalculateNextRun(DateTimeOffset.UtcNow, options.RunAt, options.TimeZone);

        return new OcrDashboardVm
        {
            WithoutOcrCount = items.Count(x => x.OcrStatus is "NONE"),
            PendingCount = items.Count(x => x.OcrStatus is "PENDING"),
            ProcessingCount = items.Count(x => x.OcrStatus is "PROCESSING" or "RUNNING"),
            CompletedCount = items.Count(x => x.OcrStatus is "COMPLETED" && x.HasOcrText),
            ErrorCount = items.Count(x => x.OcrStatus is "ERROR" or "FAILED" or "FAILURE"),
            AutoScheduleEnabled = options.Enabled,
            AutoScheduleRunAt = string.IsNullOrWhiteSpace(options.RunAt) ? "--:--" : options.RunAt,
            NextAutoRun = nextRun,
            Filter = filter,
            Items = items
        };
    }

    public async Task<IReadOnlyList<OcrQueueItemVm>> GetQueueAsync(Guid tenantId, OcrDashboardFilter filter, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var schema = await LoadSchemaAsync(conn, ct);

        if (!schema.HasDocument || !schema.HasDocumentVersion)
            return Array.Empty<OcrQueueItemVm>();

        var predicates = new List<string>
        {
            "d.tenant_id = @tenantId",
            "coalesce(d.reg_status, 'A') = 'A'"
        };

        if (!string.IsNullOrWhiteSpace(filter.Folder) && schema.HasFolder)
            predicates.Add("f.name ilike '%' || @folder || '%'");
        if (filter.From.HasValue)
            predicates.Add($"coalesce({schema.UploadedAtExpr}, d.created_at) >= @from");
        if (filter.To.HasValue)
            predicates.Add($"coalesce({schema.UploadedAtExpr}, d.created_at) < @to");
        if (!string.IsNullOrWhiteSpace(filter.DocumentType) && schema.HasDocumentType)
            predicates.Add("d.document_type ilike '%' || @documentType || '%'");
        if (filter.OnlyPartialDocuments && schema.HasPartialColumns)
            predicates.Add("coalesce(v.is_partial_document, false) = true");

        var where = string.Join(" AND ", predicates);
        var folderJoin = schema.HasFolder ? "left join ged.folder f on f.tenant_id = d.tenant_id and f.id = d.folder_id" : "left join (select null::uuid id, null::uuid tenant_id, null::text name where false) f on false";
        var ocrJoin = schema.HasOcrJob
            ? """
left join lateral (
    select j.document_version_id,
           j.status::text as status,
           j.requested_at,
           j.finished_at,
           j.error_message
    from ged.ocr_job j
    where j.tenant_id = d.tenant_id
      and j.document_version_id = v.id
    order by coalesce(j.finished_at, j.requested_at) desc nulls last
    limit 1
) oj on true
"""
            : "left join (select null::uuid document_version_id, null::text status, null::timestamptz requested_at, null::timestamptz finished_at, null::text error_message where false) oj on false";
        var documentSearchJoin = schema.HasDocumentSearch && schema.HasDocumentSearchOcrText
            ? $"""
left join lateral (
    select ds.ocr_text
    from ged.document_search ds
    where ds.tenant_id = d.tenant_id
      and ({schema.DocumentSearchVersionPredicate} or {schema.DocumentSearchDocumentPredicate})
    order by {schema.DocumentSearchOrderExpr} desc nulls last
    limit 1
) ds on true
"""
            : "left join (select null::text ocr_text where false) ds on false";
        var partialJoin = schema.HasDocumentPartialPart && schema.HasDocumentSearch && schema.HasDocumentSearchOcrText
            ? $"""
left join lateral (
    select count(*)::int as total_parts,
           count(*) filter (where nullif(btrim(coalesce(pds.ocr_text, '')), '') is not null)::int as parts_with_ocr
    from ged.document_partial_part pp
    left join ged.document_search pds on pds.tenant_id = pp.tenant_id and ({schema.DocumentSearchPartialPredicate})
    where pp.tenant_id = d.tenant_id
      and pp.document_id = d.id
      and coalesce(pp.reg_status, 'A') = 'A'
) ps on true
"""
            : "left join lateral (select 0::int as total_parts, 0::int as parts_with_ocr) ps on true";

        var documentTypeExpr = schema.HasDocumentType ? "d.document_type" : "null::text";
        var sizeExpr = schema.SizeBytesExpr;
        var uploadedByExpr = schema.CreatedByExpr;
        var isPartialExpr = schema.HasPartialColumns ? "coalesce(v.is_partial_document, false)" : "false";
        var partNumberExpr = schema.HasPartialColumns ? "v.partial_part_number" : "null::int";
        var totalPartsExpr = schema.HasPartialColumns ? "v.partial_total_parts" : "null::int";

        var sql = $"""
select
    d.id as "DocumentId",
    v.id as "VersionId",
    null::uuid as "JobId",
    coalesce(nullif(d.title, ''), v.file_name, 'Documento sem título') as "DocumentTitle",
    coalesce(v.file_name, d.title, 'arquivo') as "FileName",
    f.name as "FolderName",
    upper(coalesce(oj.status, case when nullif(btrim(coalesce(ds.ocr_text, '')), '') is not null then 'COMPLETED' else 'NONE' end)) as "OcrStatus",
    oj.requested_at as "RequestedAt",
    oj.finished_at as "FinishedAt",
    oj.error_message as "ErrorMessage",
    {isPartialExpr} as "IsPartialDocument",
    {partNumberExpr} as "PartNumber",
    {totalPartsExpr} as "TotalParts",
    (nullif(btrim(coalesce(ds.ocr_text, '')), '') is not null) as "HasOcrText",
    {documentTypeExpr} as "DocumentType",
    {sizeExpr} as "SizeBytes",
    {uploadedByExpr} as "UploadedBy",
    coalesce(ps.total_parts, 0) as "PartialTotalPartsCount",
    coalesce(ps.parts_with_ocr, 0) as "PartialPartsWithOcrCount"
from ged.document d
join ged.document_version v on v.tenant_id = d.tenant_id and v.id = d.current_version_id
{folderJoin}
{ocrJoin}
{documentSearchJoin}
{partialJoin}
where {where}
order by coalesce(oj.requested_at, {schema.UploadedAtExpr}, d.created_at) desc nulls last
limit 250;
""";

        try
        {
            var rows = (await conn.QueryAsync<OcrDashboardRow>(new CommandDefinition(
                sql,
                new
                {
                    tenantId,
                    folder = filter.Folder,
                    from = filter.From,
                    to = filter.To?.AddDays(1),
                    documentType = filter.DocumentType
                },
                cancellationToken: ct))).ToList();

            var items = rows.Select(MapRow).Where(x => MatchesFilter(x, filter)).ToList();
            return items;
        }
        catch (PostgresException ex) when (ex.SqlState is "42703" or "42P01" or "42883")
        {
            _logger.LogWarning(ex, "Central OCR indisponível por schema incompleto. Tenant={TenantId}", tenantId);
            return Array.Empty<OcrQueueItemVm>();
        }
    }

    private static bool MatchesFilter(OcrQueueItemVm item, OcrDashboardFilter filter)
    {
        if (filter.OnlyErrors && item.OcrStatus is not ("ERROR" or "FAILED" or "FAILURE")) return false;
        if (filter.OnlyWithoutOcr && item.OcrStatus is not "NONE") return false;
        if (!string.IsNullOrWhiteSpace(filter.Status) && !string.Equals(item.OcrStatus, filter.Status, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static OcrQueueItemVm MapRow(OcrDashboardRow row)
    {
        var status = string.IsNullOrWhiteSpace(row.OcrStatus) ? "NONE" : row.OcrStatus.Trim().ToUpperInvariant();
        var hasPartialOcr = row.PartialPartsWithOcrCount > 0;
        var partialTotal = row.PartialTotalPartsCount > 0 ? row.PartialTotalPartsCount : row.TotalParts.GetValueOrDefault();

        var (text, css) = ResolveStatus(status, row.HasOcrText, row.IsPartialDocument || partialTotal > 0, row.PartialPartsWithOcrCount, partialTotal);

        return new OcrQueueItemVm
        {
            DocumentId = row.DocumentId,
            VersionId = row.VersionId,
            JobId = row.JobId,
            DocumentTitle = row.DocumentTitle,
            FileName = row.FileName,
            FolderName = row.FolderName,
            OcrStatus = hasPartialOcr && status == "NONE" ? "PARTIAL" : status,
            OcrStatusText = text,
            OcrStatusCss = css,
            RequestedAt = row.RequestedAt,
            FinishedAt = row.FinishedAt,
            ErrorMessage = row.ErrorMessage,
            IsPartialDocument = row.IsPartialDocument || partialTotal > 0,
            PartNumber = row.PartNumber,
            TotalParts = partialTotal > 0 ? partialTotal : row.TotalParts,
            HasOcrText = row.HasOcrText || hasPartialOcr,
            ActionUrl = $"/Ged/Details/{row.DocumentId}",
            DocumentType = row.DocumentType,
            SizeBytes = row.SizeBytes,
            UploadedBy = row.UploadedBy
        };
    }

    private static (string Text, string Css) ResolveStatus(string status, bool hasText, bool isPartial, int partsWithOcr, int totalParts)
    {
        if (isPartial && totalParts > 0)
        {
            if (hasText) return ("OCR consolidado", "bg-success");
            if (partsWithOcr == 0) return ("Sem OCR nas partes", "bg-secondary");
            if (partsWithOcr >= totalParts) return ("OCR disponível nas partes", "bg-success");
            return ($"OCR parcial {partsWithOcr}/{totalParts}", "bg-warning text-dark");
        }

        return status switch
        {
            "COMPLETED" when hasText => ("OCR disponível", "bg-success"),
            "COMPLETED" => ("Concluído sem texto", "bg-warning text-dark"),
            "PENDING" or "QUEUED" => ("Pendente", "bg-warning text-dark"),
            "PROCESSING" or "RUNNING" => ("Em processamento", "bg-info text-dark"),
            "ERROR" or "FAILED" or "FAILURE" => ("Erro", "bg-danger"),
            "CANCELED" or "CANCELLED" => ("Cancelado", "bg-secondary"),
            _ when hasText => ("OCR disponível", "bg-success"),
            _ => ("Sem OCR", "bg-secondary")
        };
    }

    private async Task<OcrSchema> LoadSchemaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = """
select table_name as "TableName", column_name as "ColumnName"
from information_schema.columns
where table_schema = 'ged'
  and table_name in ('document', 'document_version', 'folder', 'document_search', 'ocr_job', 'document_partial_part');
""";
        var rows = await conn.QueryAsync<SchemaColumn>(new CommandDefinition(sql, cancellationToken: ct));
        var columns = rows.GroupBy(x => x.TableName).ToDictionary(g => g.Key, g => g.Select(x => x.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        bool HasTable(string table) => columns.ContainsKey(table);
        bool HasColumn(string table, string column) => columns.TryGetValue(table, out var set) && set.Contains(column);

        var versionColumn = HasColumn("document_search", "document_version_id") ? "document_version_id" : HasColumn("document_search", "version_id") ? "version_id" : null;
        var hasDocumentSearchDocumentId = HasColumn("document_search", "document_id");
        var hasUpdatedAt = HasColumn("document_search", "updated_at");

        return new OcrSchema
        {
            HasDocument = HasTable("document"),
            HasDocumentVersion = HasTable("document_version"),
            HasFolder = HasTable("folder"),
            HasOcrJob = HasTable("ocr_job"),
            HasDocumentSearch = HasTable("document_search"),
            HasDocumentSearchOcrText = HasColumn("document_search", "ocr_text"),
            HasDocumentPartialPart = HasTable("document_partial_part"),
            HasDocumentType = HasColumn("document", "document_type"),
            HasPartialColumns = HasColumn("document_version", "is_partial_document"),
            UploadedAtExpr = HasColumn("document_version", "uploaded_at_utc") ? "v.uploaded_at_utc" : HasColumn("document_version", "created_at_utc") ? "v.created_at_utc" : "v.created_at",
            SizeBytesExpr = HasColumn("document_version", "file_size_bytes") ? "v.file_size_bytes" : HasColumn("document_version", "size_bytes") ? "v.size_bytes" : "null::bigint",
            CreatedByExpr = HasColumn("document_version", "created_by_name") ? "v.created_by_name" : HasColumn("document_version", "uploaded_by_name") ? "v.uploaded_by_name" : "null::text",
            DocumentSearchVersionPredicate = versionColumn is null ? "false" : $"ds.{versionColumn} = v.id",
            DocumentSearchDocumentPredicate = hasDocumentSearchDocumentId ? "ds.document_id = d.id" : "false",
            DocumentSearchPartialPredicate = versionColumn is null ? "false" : $"pds.{versionColumn} = pp.version_id",
            DocumentSearchOrderExpr = hasUpdatedAt ? "ds.updated_at" : "null::timestamptz"
        };
    }

    private sealed class SchemaColumn
    {
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
    }

    private sealed class OcrSchema
    {
        public bool HasDocument { get; set; }
        public bool HasDocumentVersion { get; set; }
        public bool HasFolder { get; set; }
        public bool HasOcrJob { get; set; }
        public bool HasDocumentSearch { get; set; }
        public bool HasDocumentSearchOcrText { get; set; }
        public bool HasDocumentPartialPart { get; set; }
        public bool HasDocumentType { get; set; }
        public bool HasPartialColumns { get; set; }
        public string UploadedAtExpr { get; set; } = "v.created_at";
        public string SizeBytesExpr { get; set; } = "null::bigint";
        public string CreatedByExpr { get; set; } = "null::text";
        public string DocumentSearchVersionPredicate { get; set; } = "false";
        public string DocumentSearchDocumentPredicate { get; set; } = "false";
        public string DocumentSearchPartialPredicate { get; set; } = "false";
        public string DocumentSearchOrderExpr { get; set; } = "null::timestamptz";
    }

    private sealed class OcrDashboardRow
    {
        public Guid DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public Guid? JobId { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? FolderName { get; set; }
        public string OcrStatus { get; set; } = "NONE";
        public DateTimeOffset? RequestedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsPartialDocument { get; set; }
        public int? PartNumber { get; set; }
        public int? TotalParts { get; set; }
        public bool HasOcrText { get; set; }
        public string? DocumentType { get; set; }
        public long? SizeBytes { get; set; }
        public string? UploadedBy { get; set; }
        public int PartialTotalPartsCount { get; set; }
        public int PartialPartsWithOcrCount { get; set; }
    }
}
