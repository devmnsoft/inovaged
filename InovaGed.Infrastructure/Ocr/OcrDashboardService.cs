using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrDashboardService : IOcrDashboardService, IOcrStatusResolver
{
    private readonly IDbConnectionFactory _db;
    private readonly IOptionsMonitor<OcrAutoScheduleOptions> _options;
    private readonly ILogger<OcrDashboardService> _logger;

    public OcrDashboardService(IDbConnectionFactory db, IOptionsMonitor<OcrAutoScheduleOptions> options, ILogger<OcrDashboardService> logger)
    {
        _db = db;
        _options = options;
        _logger = logger;
    }

    public async Task<OcrDashboardVm> GetDashboardAsync(Guid tenantId, OcrDashboardFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        var allFilter = new OcrDashboardFilter
        {
            Folder = filter.Folder,
            FolderId = filter.FolderId,
            Search = filter.Search,
            From = filter.From,
            To = filter.To,
            DocumentType = filter.DocumentType,
            Page = 1,
            PageSize = 500
        };
        var allItems = await LoadQueueAsync(tenantId, allFilter, applyStatusFilters: false, ct);
        var items = ApplyFilters(allItems, filter).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        var options = _options.CurrentValue;
        var nextRun = SafeNextRun(options);
        var lastRun = await TryGetLastAutoRunAsync(tenantId, ct);

        return new OcrDashboardVm
        {
            WithoutOcrCount = allItems.Count(x => x.OcrStatus is "NONE"),
            PendingCount = allItems.Count(x => x.OcrStatus is "PENDING" or "QUEUED"),
            ProcessingCount = allItems.Count(x => x.OcrStatus is "PROCESSING" or "RUNNING"),
            CompletedCount = allItems.Count(x => x.OcrStatus is "COMPLETED" && x.HasOcrText),
            CompletedWithoutTextCount = allItems.Count(x => x.OcrStatus is "COMPLETED" && !x.HasOcrText),
            ErrorCount = allItems.Count(x => x.HasOcrError),
            PartialOcrCount = allItems.Count(x => x.IsPartialDocument && x.PartsWithOcr.GetValueOrDefault() > 0 && x.PartsWithOcr.GetValueOrDefault() < x.TotalParts.GetValueOrDefault()),
            AutoScheduleEnabled = options.Enabled,
            AutoScheduleRunAt = string.IsNullOrWhiteSpace(options.RunAt) ? "18:00" : options.RunAt,
            NextAutoRun = nextRun,
            LastAutoRun = lastRun?.StartedAtUtc,
            LastAutoRunEnqueuedCount = lastRun?.EnqueuedCount ?? 0,
            AutoScheduleWarning = lastRun?.SchemaWarning,
            Filter = filter,
            Items = items
        };
    }

    public async Task<IReadOnlyList<OcrQueueItemVm>> GetQueueAsync(Guid tenantId, OcrDashboardFilter filter, CancellationToken ct)
        => ApplyFilters(await LoadQueueAsync(tenantId, NormalizeFilter(filter), applyStatusFilters: false, ct), NormalizeFilter(filter)).ToList();

    public async Task<OcrJobDetailsVm?> GetJobAsync(Guid tenantId, string jobId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return null;
        const string sql = """
select
    j.id::text as "JobId",
    d.id as "DocumentId",
    j.document_version_id as "VersionId",
    upper(j.status::text) as "Status",
    coalesce(v.file_name, d.title, 'arquivo') as "FileName",
    j.error_message as "ErrorMessage",
    j.requested_at as "RequestedAt",
    j.started_at as "StartedAt",
    j.finished_at as "FinishedAt"
from ged.ocr_job j
left join ged.document_version v on v.tenant_id = j.tenant_id and v.id = j.document_version_id
left join ged.document d on d.tenant_id = j.tenant_id and d.id = v.document_id
where j.tenant_id = @tenantId
  and j.id::text = @jobId
limit 1;
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var job = await conn.QuerySingleOrDefaultAsync<OcrJobDetailsVm>(new CommandDefinition(sql, new { tenantId, jobId }, cancellationToken: ct));
            if (job is not null) job.StatusText = StatusText(job.Status, !string.IsNullOrWhiteSpace(job.ErrorMessage), false, 0, 0).Text;
            return job;
        }
        catch (PostgresException ex) when (ex.SqlState is "42703" or "42P01" or "42883" or "22P02")
        {
            _logger.LogWarning(ex, "Detalhe do job OCR indisponível. Tenant={TenantId} JobId={JobId}", tenantId, jobId);
            return null;
        }
    }

    public async Task<string?> GetOcrTextByVersionAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var schema = await LoadSchemaAsync(conn, ct);
        if (!schema.HasDocumentSearch || !schema.HasDocumentSearchOcrText || schema.DocumentSearchVersionColumn is null) return null;

        var sql = $"""
select ds.ocr_text
from ged.document_search ds
join ged.document_version v on v.tenant_id = ds.tenant_id and v.id = ds.{schema.DocumentSearchVersionColumn}
join ged.document d on d.tenant_id = v.tenant_id and d.id = v.document_id
where ds.tenant_id = @tenantId
  and ds.{schema.DocumentSearchVersionColumn} = @versionId
  and coalesce(d.reg_status, 'A') = 'A'
limit 1;
""";
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
    }

    public async Task<OcrResolvedStatusVm> ResolveForVersionAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var schema = await LoadSchemaAsync(conn, ct);
        var item = (await QueryRowsAsync(conn, schema, tenantId, new OcrDashboardFilter { Page = 1, PageSize = 500 }, ct))
            .Select(MapRow)
            .FirstOrDefault(x => x.VersionId == versionId);
        return ToResolved(item, versionId: versionId);
    }

    public async Task<OcrResolvedStatusVm> ResolveForDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var schema = await LoadSchemaAsync(conn, ct);
        var item = (await QueryRowsAsync(conn, schema, tenantId, new OcrDashboardFilter { Page = 1, PageSize = 500 }, ct))
            .Select(MapRow)
            .FirstOrDefault(x => x.DocumentId == documentId && !x.IsPartialDocument);
        return ToResolved(item, documentId: documentId);
    }

    public async Task<OcrResolvedStatusVm> ResolveForPartialDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var schema = await LoadSchemaAsync(conn, ct);
        var parts = (await QueryRowsAsync(conn, schema, tenantId, new OcrDashboardFilter { Page = 1, PageSize = 500, OnlyPartial = true }, ct))
            .Select(MapRow)
            .Where(x => x.DocumentId == documentId && x.IsPartialDocument)
            .ToList();
        var first = parts.FirstOrDefault();
        if (first is null) return ToResolved(null, documentId: documentId);
        var total = parts.Max(x => x.TotalParts.GetValueOrDefault(parts.Count));
        var withOcr = parts.Count(x => x.HasOcrText);
        var (text, css) = StatusText("NONE", false, true, withOcr, total);
        return new OcrResolvedStatusVm { DocumentId = documentId, Status = withOcr == 0 ? "NONE" : withOcr >= total ? "COMPLETED" : "PARTIAL", StatusText = text, StatusCss = css, HasOcrText = withOcr > 0, TotalParts = total, PartsWithOcr = withOcr, PartialSummaryText = text };
    }

    private async Task<IReadOnlyList<OcrQueueItemVm>> LoadQueueAsync(Guid tenantId, OcrDashboardFilter filter, bool applyStatusFilters, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var schema = await LoadSchemaAsync(conn, ct);
        if (!schema.HasDocument || !schema.HasDocumentVersion) return Array.Empty<OcrQueueItemVm>();

        try
        {
            var rows = await QueryRowsAsync(conn, schema, tenantId, filter, ct);
            var items = rows.Select(MapRow).ToList();
            return applyStatusFilters ? ApplyFilters(items, filter).ToList() : items;
        }
        catch (PostgresException ex) when (ex.SqlState is "42703" or "42P01" or "42883")
        {
            _logger.LogWarning(ex, "Central OCR indisponível por schema incompleto. Tenant={TenantId}", tenantId);
            return Array.Empty<OcrQueueItemVm>();
        }
    }

    private async Task<IReadOnlyList<OcrDashboardRow>> QueryRowsAsync(NpgsqlConnection conn, OcrSchema schema, Guid tenantId, OcrDashboardFilter filter, CancellationToken ct)
    {
        var predicates = BuildPredicates(schema, filter, "d", "v", "f");
        var normalSql = BuildDocumentSelect(schema, string.Join(" AND ", predicates), isPart: false);
        var sql = normalSql;
        if (schema.HasDocumentPartialPart)
        {
            var partPredicates = BuildPredicates(schema, filter, "d", "pv", "f");
            sql += "\nunion all\n" + BuildDocumentSelect(schema, string.Join(" AND ", partPredicates), isPart: true);
        }

        sql = $"""
select
    q."DocumentId", q."VersionId", q."JobId", q."DocumentTitle", q."FileName", q."FolderName", q."OcrStatus",
    q."RequestedAt", q."StartedAt", q."FinishedAt", q."ErrorMessage", q."IsPartialDocument", q."PartNumber",
    q."TotalParts", q."HasOcrText", q."DocumentType", q."SizeBytes", q."UploadedBy",
    q."PartialTotalPartsCount", q."PartialPartsWithOcrCount"
from (
{sql}
) q
order by coalesce(q."RequestedAt", q."FinishedAt") desc nulls last, q."DocumentTitle"
limit @limit offset @offset;
""";

        return (await conn.QueryAsync<OcrDashboardRow>(new CommandDefinition(sql, new
        {
            tenantId,
            folder = filter.Folder,
            folderId = filter.FolderId,
            search = filter.Search,
            from = filter.From,
            to = filter.To?.AddDays(1),
            documentType = filter.DocumentType,
            limit = Math.Clamp(filter.PageSize, 1, 500),
            offset = Math.Max(0, (filter.Page - 1) * filter.PageSize)
        }, cancellationToken: ct))).ToList();
    }

    private static List<string> BuildPredicates(OcrSchema schema, OcrDashboardFilter filter, string documentAlias, string versionAlias, string folderAlias)
    {
        var predicates = new List<string> { $"{documentAlias}.tenant_id = @tenantId", $"coalesce({documentAlias}.reg_status, 'A') = 'A'" };
        if (filter.FolderId.HasValue && schema.HasFolder) predicates.Add($"{documentAlias}.folder_id = @folderId");
        if (!string.IsNullOrWhiteSpace(filter.Folder) && schema.HasFolder) predicates.Add($"{folderAlias}.name ilike '%' || @folder || '%'");
        if (filter.From.HasValue) predicates.Add($"coalesce({schema.UploadedAtExpr(versionAlias)}, {documentAlias}.created_at) >= @from");
        if (filter.To.HasValue) predicates.Add($"coalesce({schema.UploadedAtExpr(versionAlias)}, {documentAlias}.created_at) < @to");
        if (!string.IsNullOrWhiteSpace(filter.DocumentType) && schema.HasDocumentType) predicates.Add($"{documentAlias}.document_type ilike '%' || @documentType || '%'");
        if (!string.IsNullOrWhiteSpace(filter.Search)) predicates.Add($"(coalesce({documentAlias}.title, '') ilike '%' || @search || '%' or coalesce({versionAlias}.file_name, '') ilike '%' || @search || '%')");
        return predicates;
    }

    private static string BuildDocumentSelect(OcrSchema schema, string where, bool isPart)
    {
        var v = isPart ? "pv" : "v";
        var folderJoin = schema.HasFolder ? "left join ged.folder f on f.tenant_id = d.tenant_id and f.id = d.folder_id" : "left join (select null::uuid id, null::uuid tenant_id, null::text name where false) f on false";
        var versionJoin = isPart
            ? "join ged.document_partial_part pp on pp.tenant_id = d.tenant_id and pp.document_id = d.id and coalesce(pp.reg_status, 'A') = 'A' join ged.document_version pv on pv.tenant_id = pp.tenant_id and pv.id = pp.version_id"
            : "join ged.document_version v on v.tenant_id = d.tenant_id and v.id = d.current_version_id";
        var ocrJoin = schema.HasOcrJob ? $"""
left join lateral (
    select j.id::text as job_id, j.status::text as status, {schema.OcrRequestedAtExpr("j")} as requested_at, {schema.OcrStartedAtExpr("j")} as started_at, {schema.OcrFinishedAtExpr("j")} as finished_at, {schema.OcrErrorMessageExpr("j")} as error_message
    from ged.ocr_job j
    where j.tenant_id = d.tenant_id and j.document_version_id = {v}.id
    order by coalesce({schema.OcrFinishedAtExpr("j")}, {schema.OcrStartedAtExpr("j")}, {schema.OcrRequestedAtExpr("j")}) desc nulls last
    limit 1
) oj on true
""" : "left join (select null::text job_id, null::text status, null::timestamptz requested_at, null::timestamptz started_at, null::timestamptz finished_at, null::text error_message where false) oj on false";
        var searchJoin = schema.HasDocumentSearch && schema.HasDocumentSearchOcrText && schema.DocumentSearchVersionColumn is not null ? $"""
left join lateral (
    select ds.ocr_text
    from ged.document_search ds
    where ds.tenant_id = d.tenant_id and ds.{schema.DocumentSearchVersionColumn} = {v}.id
    order by {schema.DocumentSearchOrderExpr("ds")} desc nulls last
    limit 1
) ds on true
""" : "left join (select null::text ocr_text where false) ds on false";
        var partStatsJoin = schema.HasDocumentPartialPart && schema.HasDocumentSearch && schema.HasDocumentSearchOcrText && schema.DocumentSearchVersionColumn is not null ? $"""
left join lateral (
    select count(*)::int as total_parts,
           count(*) filter (where nullif(btrim(coalesce(pds.ocr_text, '')), '') is not null)::int as parts_with_ocr
    from ged.document_partial_part pp2
    left join ged.document_search pds on pds.tenant_id = pp2.tenant_id and pds.{schema.DocumentSearchVersionColumn} = pp2.version_id
    where pp2.tenant_id = d.tenant_id and pp2.document_id = d.id and coalesce(pp2.reg_status, 'A') = 'A'
) ps on true
""" : "left join lateral (select 0::int as total_parts, 0::int as parts_with_ocr) ps on true";

        var titleExpr = isPart ? "coalesce(nullif(d.title, ''), pv.file_name, 'Documento sem título') || ' — Parte ' || pp.part_number::text || coalesce('/' || pp.total_parts::text, '')" : $"coalesce(nullif(d.title, ''), {v}.file_name, 'Documento sem título')";
        var fileExpr = isPart ? $"coalesce(pp.file_name, {v}.file_name, d.title, 'arquivo')" : $"coalesce({v}.file_name, d.title, 'arquivo')";
        var docTypeExpr = schema.HasDocumentType ? "d.document_type" : "null::text";
        var sizeExpr = isPart ? "coalesce(pp.size_bytes, " + schema.SizeBytesExpr(v) + ")" : schema.SizeBytesExpr(v);
        var uploadedByExpr = schema.CreatedByExpr(v);
        var isPartialExpr = isPart ? "true" : (schema.HasPartialColumns ? $"coalesce({v}.is_partial_document, false)" : "false");
        var partNumberExpr = isPart ? "pp.part_number" : (schema.PartNumberColumn is not null ? $"{v}.{schema.PartNumberColumn}" : "null::int");
        var totalPartsExpr = isPart ? "pp.total_parts" : (schema.TotalPartsColumn is not null ? $"{v}.{schema.TotalPartsColumn}" : "null::int");

        return $"""
select
    d.id as "DocumentId",
    {v}.id as "VersionId",
    oj.job_id as "JobId",
    {titleExpr} as "DocumentTitle",
    {fileExpr} as "FileName",
    f.name as "FolderName",
    upper(coalesce(oj.status, case when nullif(btrim(coalesce(ds.ocr_text, '')), '') is not null then 'COMPLETED' else 'NONE' end)) as "OcrStatus",
    oj.requested_at as "RequestedAt",
    oj.started_at as "StartedAt",
    oj.finished_at as "FinishedAt",
    oj.error_message as "ErrorMessage",
    {isPartialExpr} as "IsPartialDocument",
    {partNumberExpr} as "PartNumber",
    {totalPartsExpr} as "TotalParts",
    (nullif(btrim(coalesce(ds.ocr_text, '')), '') is not null) as "HasOcrText",
    {docTypeExpr} as "DocumentType",
    {sizeExpr} as "SizeBytes",
    {uploadedByExpr} as "UploadedBy",
    coalesce(ps.total_parts, 0) as "PartialTotalPartsCount",
    coalesce(ps.parts_with_ocr, 0) as "PartialPartsWithOcrCount"
from ged.document d
{versionJoin}
{folderJoin}
{ocrJoin}
{searchJoin}
{partStatsJoin}
where {where}
""";
    }

    private static IEnumerable<OcrQueueItemVm> ApplyFilters(IEnumerable<OcrQueueItemVm> items, OcrDashboardFilter filter)
    {
        if (filter.OnlyErrors) items = items.Where(x => x.HasOcrError);
        if (filter.OnlyWithoutOcr) items = items.Where(x => x.OcrStatus == "NONE");
        if (filter.OnlyPartial) items = items.Where(x => x.IsPartialDocument);
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim().ToUpperInvariant();
            items = status switch
            {
                "ERROR" => items.Where(x => x.HasOcrError),
                "WITHOUT_TEXT" or "COMPLETED_WITHOUT_TEXT" => items.Where(x => x.OcrStatus == "COMPLETED" && !x.HasOcrText),
                _ => items.Where(x => string.Equals(x.OcrStatus, status, StringComparison.OrdinalIgnoreCase))
            };
        }
        return items;
    }

    private static OcrQueueItemVm MapRow(OcrDashboardRow row)
    {
        var status = string.IsNullOrWhiteSpace(row.OcrStatus) ? "NONE" : row.OcrStatus.Trim().ToUpperInvariant();
        var totalParts = row.PartialTotalPartsCount > 0 ? row.PartialTotalPartsCount : row.TotalParts.GetValueOrDefault();
        var partsWithOcr = row.PartialPartsWithOcrCount;
        var isPartial = row.IsPartialDocument || totalParts > 0;
        var (text, css) = StatusText(status, row.HasOcrText, isPartial, partsWithOcr, totalParts);
        var normalizedStatus = isPartial && partsWithOcr > 0 && partsWithOcr < totalParts && status == "NONE" ? "PARTIAL" : status;

        return new OcrQueueItemVm
        {
            DocumentId = row.DocumentId,
            VersionId = row.VersionId,
            JobId = row.JobId,
            DocumentTitle = row.DocumentTitle,
            FileName = row.FileName,
            FolderName = row.FolderName,
            DocumentType = row.DocumentType,
            OcrStatus = normalizedStatus,
            OcrStatusText = text,
            OcrStatusCss = css,
            HasOcrText = row.HasOcrText,
            HasOcrError = status is "ERROR" or "FAILED" or "FAILURE",
            ErrorMessage = row.ErrorMessage,
            RequestedAt = row.RequestedAt,
            StartedAt = row.StartedAt,
            FinishedAt = row.FinishedAt,
            IsPartialDocument = isPartial,
            PartNumber = row.PartNumber,
            TotalParts = totalParts > 0 ? totalParts : row.TotalParts,
            PartsWithOcr = partsWithOcr,
            PartialSummaryText = isPartial ? text : null,
            ActionUrl = $"/Ged/Details/{row.DocumentId}",
            SizeBytes = row.SizeBytes,
            UploadedBy = row.UploadedBy
        };
    }

    private static (string Text, string Css) StatusText(string status, bool hasText, bool isPartial, int partsWithOcr, int totalParts)
    {
        if (isPartial && totalParts > 0 && status is ("NONE" or "COMPLETED"))
        {
            if (hasText) return ("OCR consolidado", "bg-success");
            if (partsWithOcr == 0) return ("Sem OCR nas partes", "bg-secondary");
            if (partsWithOcr >= totalParts) return ("OCR disponível nas partes", "bg-success");
            return ($"OCR parcial {partsWithOcr}/{totalParts}", "bg-warning text-dark");
        }

        return status switch
        {
            "COMPLETED" when hasText => ("Concluído", "bg-success"),
            "COMPLETED" => ("Concluído sem texto", "bg-warning text-dark"),
            "PENDING" or "QUEUED" => ("Pendente", "bg-warning text-dark"),
            "PROCESSING" or "RUNNING" => ("Em processamento", "bg-info text-dark"),
            "ERROR" or "FAILED" or "FAILURE" => ("Erro", "bg-danger"),
            "CANCELED" or "CANCELLED" => ("Cancelado", "bg-secondary"),
            _ when hasText => ("Concluído", "bg-success"),
            _ => ("Sem OCR", "bg-secondary")
        };
    }

    private static OcrResolvedStatusVm ToResolved(OcrQueueItemVm? item, Guid? documentId = null, Guid? versionId = null)
        => item is null
            ? new OcrResolvedStatusVm { DocumentId = documentId, VersionId = versionId }
            : new OcrResolvedStatusVm { DocumentId = item.DocumentId, VersionId = item.VersionId, Status = item.OcrStatus, StatusText = item.OcrStatusText, StatusCss = item.OcrStatusCss, HasOcrText = item.HasOcrText, HasError = item.HasOcrError, TotalParts = item.TotalParts, PartsWithOcr = item.PartsWithOcr, PartialSummaryText = item.PartialSummaryText };

    private static OcrDashboardFilter NormalizeFilter(OcrDashboardFilter? filter)
    {
        filter ??= new OcrDashboardFilter();
        filter.Page = filter.Page <= 0 ? 1 : filter.Page;
        filter.PageSize = filter.PageSize <= 0 ? 50 : Math.Clamp(filter.PageSize, 1, 250);
        return filter;
    }

    private DateTimeOffset? SafeNextRun(OcrAutoScheduleOptions options)
    {
        try { return OcrAutoScheduleClock.CalculateNextRun(DateTimeOffset.UtcNow, options.RunAt, options.TimeZone); }
        catch { return null; }
    }

    private async Task<AutoRunInfo?> TryGetLastAutoRunAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = """
select started_at_utc as "StartedAtUtc", enqueued_count as "EnqueuedCount", null::text as "SchemaWarning"
from ged.ocr_auto_schedule_run
where tenant_id = @tenantId
order by started_at_utc desc
limit 1;
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<AutoRunInfo>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState is "42P01" or "42703")
        {
            return new AutoRunInfo { SchemaWarning = "Agendamento OCR ainda não configurado. Execute as migrations." };
        }
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
        var searchVersionColumn = HasColumn("document_search", "document_version_id") ? "document_version_id" : HasColumn("document_search", "version_id") ? "version_id" : null;
        return new OcrSchema
        {
            HasDocument = HasTable("document"),
            HasDocumentVersion = HasTable("document_version"),
            HasFolder = HasTable("folder"),
            HasOcrJob = HasTable("ocr_job"),
            HasDocumentSearch = HasTable("document_search"),
            HasDocumentSearchOcrText = HasColumn("document_search", "ocr_text"),
            HasDocumentPartialPart = HasTable("document_partial_part") && HasColumn("document_partial_part", "version_id"),
            HasDocumentType = HasColumn("document", "document_type"),
            HasPartialColumns = HasColumn("document_version", "is_partial_document"),
            HasPartNumber = HasColumn("document_version", "part_number") || HasColumn("document_version", "partial_part_number"),
            PartNumberColumn = HasColumn("document_version", "part_number") ? "part_number" : HasColumn("document_version", "partial_part_number") ? "partial_part_number" : null,
            HasTotalParts = HasColumn("document_version", "total_parts") || HasColumn("document_version", "partial_total_parts"),
            TotalPartsColumn = HasColumn("document_version", "total_parts") ? "total_parts" : HasColumn("document_version", "partial_total_parts") ? "partial_total_parts" : null,
            UploadedAtColumn = HasColumn("document_version", "uploaded_at_utc") ? "uploaded_at_utc" : HasColumn("document_version", "created_at_utc") ? "created_at_utc" : "created_at",
            SizeBytesColumn = HasColumn("document_version", "file_size_bytes") ? "file_size_bytes" : HasColumn("document_version", "size_bytes") ? "size_bytes" : null,
            CreatedByColumn = HasColumn("document_version", "created_by_name") ? "created_by_name" : HasColumn("document_version", "uploaded_by_name") ? "uploaded_by_name" : null,
            DocumentSearchVersionColumn = searchVersionColumn,
            DocumentSearchUpdatedAtColumn = HasColumn("document_search", "updated_at") ? "updated_at" : null,
            HasOcrRequestedAt = HasColumn("ocr_job", "requested_at"),
            HasOcrStartedAt = HasColumn("ocr_job", "started_at"),
            HasOcrFinishedAt = HasColumn("ocr_job", "finished_at"),
            HasOcrErrorMessage = HasColumn("ocr_job", "error_message")
        };
    }

    private sealed class SchemaColumn { public string TableName { get; set; } = string.Empty; public string ColumnName { get; set; } = string.Empty; }
    private sealed class AutoRunInfo { public DateTimeOffset? StartedAtUtc { get; set; } public int EnqueuedCount { get; set; } public string? SchemaWarning { get; set; } }

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
        public bool HasPartNumber { get; set; }
        public string? PartNumberColumn { get; set; }
        public bool HasTotalParts { get; set; }
        public string? TotalPartsColumn { get; set; }
        public string UploadedAtColumn { get; set; } = "created_at";
        public string? SizeBytesColumn { get; set; }
        public string? CreatedByColumn { get; set; }
        public string? DocumentSearchVersionColumn { get; set; }
        public string? DocumentSearchUpdatedAtColumn { get; set; }
        public bool HasOcrRequestedAt { get; set; }
        public bool HasOcrStartedAt { get; set; }
        public bool HasOcrFinishedAt { get; set; }
        public bool HasOcrErrorMessage { get; set; }
        public string UploadedAtExpr(string alias) => $"{alias}.{UploadedAtColumn}";
        public string SizeBytesExpr(string alias) => SizeBytesColumn is null ? "null::bigint" : $"{alias}.{SizeBytesColumn}";
        public string CreatedByExpr(string alias) => CreatedByColumn is null ? "null::text" : $"{alias}.{CreatedByColumn}";
        public string DocumentSearchOrderExpr(string alias) => DocumentSearchUpdatedAtColumn is null ? "null::timestamptz" : $"{alias}.{DocumentSearchUpdatedAtColumn}";
        public string OcrRequestedAtExpr(string alias) => HasOcrRequestedAt ? $"{alias}.requested_at" : "null::timestamptz";
        public string OcrStartedAtExpr(string alias) => HasOcrStartedAt ? $"{alias}.started_at" : "null::timestamptz";
        public string OcrFinishedAtExpr(string alias) => HasOcrFinishedAt ? $"{alias}.finished_at" : "null::timestamptz";
        public string OcrErrorMessageExpr(string alias) => HasOcrErrorMessage ? $"{alias}.error_message" : "null::text";
    }

    private sealed class OcrDashboardRow
    {
        public Guid DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public string? JobId { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? FolderName { get; set; }
        public string OcrStatus { get; set; } = "NONE";
        public DateTimeOffset? RequestedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
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
