using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrAutoScheduleRepository : IOcrAutoScheduleRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OcrAutoScheduleRepository> _logger;

    public OcrAutoScheduleRepository(IDbConnectionFactory db, ILogger<OcrAutoScheduleRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OcrAutoScheduleCandidateDto>> GetDocumentsWithoutOcrAsync(OcrAutoScheduleOptions options, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var dsJoin = await BuildDocumentSearchLateralJoinAsync(conn, "d", "v", ct);
        var partialDsJoin = await BuildDocumentSearchLateralJoinAsync(conn, "d", "v", ct);
        var activePredicate = options.OnlyActiveDocuments ? "AND COALESCE(d.reg_status, 'A') = 'A'" : string.Empty;
        var pendingFilter = BuildPendingFilter(options);
        var retryFilter = options.RetryFailedOcr
            ? "OR (upper(COALESCE(last_ocr.status::text, '')) IN ('ERROR','FAILED') AND COALESCE(last_ocr.finished_at, last_ocr.requested_at) <= now() - (@RetryFailedOlderThanHours || ' hours')::interval)"
            : string.Empty;

        var sql = $"""
WITH normal_candidates AS (
    SELECT
        d.id AS "DocumentId",
        COALESCE(d.title, v.file_name, '') AS "Title",
        v.id AS "VersionId",
        COALESCE(v.file_name, '') AS "FileName",
        v.content_type AS "ContentType",
        v.storage_path AS "StoragePath",
        (NULLIF(btrim(COALESCE(ds.ocr_text, '')), '') IS NOT NULL) AS "HasOcrText",
        last_ocr.status::text AS "LastOcrStatus",
        'DOCUMENT' AS "Source"
    FROM ged.document d
    JOIN ged.document_version v ON v.id = d.current_version_id
    LEFT JOIN LATERAL (
        SELECT j.*
        FROM ged.ocr_job j
        WHERE j.tenant_id = d.tenant_id
          AND j.document_version_id = v.id
        ORDER BY COALESCE(j.finished_at, j.requested_at) DESC NULLS LAST
        LIMIT 1
    ) last_ocr ON true
    {dsJoin}
    WHERE d.tenant_id = @TenantId
      {activePredicate}
      AND d.current_version_id IS NOT NULL
      AND lower(substring(COALESCE(v.file_name, '') from '\\.[^.]*$')) = any(@AllowedExtensions)
      AND (
          @SkipIfOcrAvailable = false
          OR NULLIF(btrim(COALESCE(ds.ocr_text, '')), '') IS NULL
          {retryFilter}
      )
      {pendingFilter}
),
partial_candidates AS (
    SELECT
        d.id AS "DocumentId",
        COALESCE(d.title, p.file_name, v.file_name, '') AS "Title",
        p.version_id AS "VersionId",
        COALESCE(p.file_name, v.file_name, '') AS "FileName",
        v.content_type AS "ContentType",
        v.storage_path AS "StoragePath",
        (NULLIF(btrim(COALESCE(ds.ocr_text, '')), '') IS NOT NULL) AS "HasOcrText",
        last_ocr.status::text AS "LastOcrStatus",
        'PARTIAL_PART' AS "Source"
    FROM ged.document_partial_part p
    JOIN ged.document d ON d.id = p.document_id AND d.tenant_id = p.tenant_id
    JOIN ged.document_version v ON v.id = p.version_id
    LEFT JOIN LATERAL (
        SELECT j.*
        FROM ged.ocr_job j
        WHERE j.tenant_id = p.tenant_id
          AND j.document_version_id = p.version_id
        ORDER BY COALESCE(j.finished_at, j.requested_at) DESC NULLS LAST
        LIMIT 1
    ) last_ocr ON true
    {partialDsJoin}
    WHERE p.tenant_id = @TenantId
      AND COALESCE(p.reg_status, 'A') = 'A'
      {activePredicate}
      AND p.version_id IS NOT NULL
      AND lower(substring(COALESCE(p.file_name, v.file_name, '') from '\\.[^.]*$')) = any(@AllowedExtensions)
      AND (
          @SkipIfOcrAvailable = false
          OR NULLIF(btrim(COALESCE(ds.ocr_text, '')), '') IS NULL
          {retryFilter}
      )
      {pendingFilter}
)
SELECT DISTINCT ON ("VersionId") *
FROM (
    SELECT * FROM partial_candidates
    UNION ALL
    SELECT * FROM normal_candidates
) q
ORDER BY "VersionId", CASE WHEN "Source" = 'PARTIAL_PART' THEN 0 ELSE 1 END
LIMIT @MaxDocumentsPerRun;
""";

        var allowed = NormalizeAllowedExtensions(options.AllowedExtensions);
        var rows = await conn.QueryAsync<OcrAutoScheduleCandidateDto>(new CommandDefinition(sql, new
        {
            options.TenantId,
            AllowedExtensions = allowed,
            MaxDocumentsPerRun = Math.Max(options.MaxDocumentsPerRun, 1),
            options.SkipIfOcrAvailable,
            options.RetryFailedOlderThanHours
        }, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<int> CountDocumentsWithoutOcrAsync(OcrAutoScheduleOptions options, CancellationToken ct)
    {
        var clone = new OcrAutoScheduleOptions
        {
            Enabled = options.Enabled,
            RunAt = options.RunAt,
            TimeZone = options.TimeZone,
            TenantId = options.TenantId,
            SystemUserId = options.SystemUserId,
            OnlyActiveDocuments = options.OnlyActiveDocuments,
            IncludeSubfolders = options.IncludeSubfolders,
            MaxDocumentsPerRun = Math.Max(options.MaxDocumentsPerRun, 500),
            BatchSize = options.BatchSize,
            SkipIfOcrJobPending = options.SkipIfOcrJobPending,
            SkipIfOcrJobProcessing = options.SkipIfOcrJobProcessing,
            SkipIfOcrAvailable = options.SkipIfOcrAvailable,
            RetryFailedOcr = options.RetryFailedOcr,
            RetryFailedOlderThanHours = options.RetryFailedOlderThanHours,
            AllowedExtensions = options.AllowedExtensions
        };
        var rows = await GetDocumentsWithoutOcrAsync(clone, ct);
        return rows.Count;
    }

    public async Task<string?> GetLatestOcrJobStatusAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(@"
SELECT upper(status::text)
FROM ged.ocr_job
WHERE tenant_id = @tenantId
  AND document_version_id = @versionId
ORDER BY COALESCE(finished_at, requested_at) DESC NULLS LAST
LIMIT 1;", new { tenantId, versionId }, cancellationToken: ct));
    }

    public async Task<bool> HasOcrAvailableAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var ds = await ResolveDocumentSearchVersionPredicateAsync(conn, "ds", "@versionId", ct);
        var dsExists = ds is null ? "false" : $"exists (select 1 from ged.document_search ds where ds.tenant_id=@tenantId and {ds} and NULLIF(btrim(COALESCE(ds.ocr_text,'')), '') IS NOT NULL)";
        var sql = $"SELECT {dsExists};";
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
    }

    public async Task<Guid> InsertRunAsync(OcrAutoScheduleRunResultDto result, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var id = result.RunId == Guid.Empty ? Guid.NewGuid() : result.RunId;
        await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO ged.ocr_auto_schedule_run
(id, tenant_id, started_at_utc, status, candidates_found, enqueued_count, skipped_count, failed_count, message, correlation_id)
VALUES
(@Id, @TenantId, @StartedAtUtc, @Status, @CandidatesFound, @EnqueuedCount, @SkippedCount, @FailedCount, @Message, @CorrelationId);", new
        {
            Id = id,
            result.TenantId,
            result.StartedAtUtc,
            result.Status,
            result.CandidatesFound,
            result.EnqueuedCount,
            result.SkippedCount,
            result.FailedCount,
            result.Message,
            result.CorrelationId
        }, cancellationToken: ct));
        return id;
    }

    public async Task UpdateRunAsync(OcrAutoScheduleRunResultDto result, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(@"
UPDATE ged.ocr_auto_schedule_run
SET finished_at_utc = @FinishedAtUtc,
    status = @Status,
    candidates_found = @CandidatesFound,
    enqueued_count = @EnqueuedCount,
    skipped_count = @SkippedCount,
    failed_count = @FailedCount,
    message = @Message,
    correlation_id = @CorrelationId
WHERE id = @RunId;", new
        {
            result.RunId,
            result.FinishedAtUtc,
            result.Status,
            result.CandidatesFound,
            result.EnqueuedCount,
            result.SkippedCount,
            result.FailedCount,
            result.Message,
            result.CorrelationId
        }, cancellationToken: ct));
    }

    public async Task InsertRunItemAsync(Guid runId, Guid tenantId, OcrAutoScheduleItemResultDto item, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO ged.ocr_auto_schedule_run_item
(run_id, tenant_id, document_id, version_id, file_name, status, reason, ocr_job_id)
VALUES
(@RunId, @TenantId, @DocumentId, @VersionId, @FileName, @Status, @Reason, @OcrJobId);", new
        {
            RunId = runId,
            TenantId = tenantId,
            item.DocumentId,
            item.VersionId,
            item.FileName,
            item.Status,
            item.Reason,
            OcrJobId = item.OcrJobId
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<OcrAutoScheduleRunSummaryDto>> GetRunHistoryAsync(Guid tenantId, int take, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<OcrAutoScheduleRunSummaryDto>(new CommandDefinition(@"
SELECT id AS ""Id"", tenant_id AS ""TenantId"", started_at_utc AS ""StartedAtUtc"", finished_at_utc AS ""FinishedAtUtc"",
       status AS ""Status"", candidates_found AS ""CandidatesFound"", enqueued_count AS ""EnqueuedCount"",
       skipped_count AS ""SkippedCount"", failed_count AS ""FailedCount"", message AS ""Message"", correlation_id AS ""CorrelationId""
FROM ged.ocr_auto_schedule_run
WHERE tenant_id = @tenantId
ORDER BY started_at_utc DESC
LIMIT @take;", new { tenantId, take = Math.Clamp(take, 1, 200) }, cancellationToken: ct));
            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Histórico do agendamento automático de OCR indisponível.");
            return Array.Empty<OcrAutoScheduleRunSummaryDto>();
        }
    }

    public async Task<OcrAutoScheduleRunSummaryDto?> GetLastRunAsync(Guid tenantId, CancellationToken ct)
        => (await GetRunHistoryAsync(tenantId, 1, ct)).FirstOrDefault();

    private static string BuildPendingFilter(OcrAutoScheduleOptions options)
    {
        var blocked = new List<string>();
        if (options.SkipIfOcrJobPending) blocked.Add("PENDING");
        if (options.SkipIfOcrJobProcessing) blocked.Add("PROCESSING");
        return blocked.Count == 0
            ? string.Empty
            : $"AND (last_ocr.id IS NULL OR upper(last_ocr.status::text) NOT IN ({string.Join(",", blocked.Select(s => $"'{s}'"))}))";
    }

    private static string[] NormalizeAllowedExtensions(IEnumerable<string>? allowed)
        => (allowed ?? Array.Empty<string>())
            .Select(e => e?.Trim().ToLowerInvariant())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.StartsWith('.') ? e : "." + e)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty(".pdf")
            .ToArray();

    private static async Task<string> BuildDocumentSearchLateralJoinAsync(System.Data.IDbConnection conn, string documentAlias, string versionAlias, CancellationToken ct)
    {
        var predicate = await ResolveDocumentSearchVersionPredicateAsync(conn, "ds", $"{versionAlias}.id", ct);
        if (predicate is null)
        {
            return "LEFT JOIN LATERAL (SELECT NULL::text AS ocr_text WHERE false) ds ON true";
        }

        return $"""
LEFT JOIN LATERAL (
    SELECT ds.ocr_text
    FROM ged.document_search ds
    WHERE ds.tenant_id = {documentAlias}.tenant_id
      AND {predicate}
    LIMIT 1
) ds ON true
""";
    }

    private static async Task<string?> ResolveDocumentSearchVersionPredicateAsync(System.Data.IDbConnection conn, string dsAlias, string versionExpression, CancellationToken ct)
    {
        var columns = (await conn.QueryAsync<string>(new CommandDefinition(@"
SELECT column_name
FROM information_schema.columns
WHERE table_schema = 'ged'
  AND table_name = 'document_search'
  AND column_name IN ('version_id', 'document_version_id');", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var predicates = new List<string>();
        if (columns.Contains("version_id")) predicates.Add($"{dsAlias}.version_id = {versionExpression}");
        if (columns.Contains("document_version_id")) predicates.Add($"{dsAlias}.document_version_id = {versionExpression}");
        return predicates.Count == 0 ? null : $"({string.Join(" OR ", predicates)})";
    }
}
