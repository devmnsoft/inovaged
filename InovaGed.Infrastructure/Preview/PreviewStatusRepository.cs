using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Preview;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Preview;

public sealed class PreviewStatusRepository : IPreviewStatusRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<PreviewStatusRepository> _logger;

    public PreviewStatusRepository(IDbConnectionFactory db, ILogger<PreviewStatusRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PreviewStatusDto?> GetAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var columns = await conn.QueryAsync<ColumnMetadataRow>(new CommandDefinition(@"
SELECT c.column_name AS ""ColumnName"", true AS ""Exists""
FROM information_schema.columns c
WHERE c.table_schema = 'ged'
  AND c.table_name = 'preview_status'
  AND c.column_name = ANY(@columns);", new
            {
                columns = new[] { "preview_path", "error_message", "error", "last_error", "error_text", "preview_error", "message", "preview_attempts", "attempts", "updated_at", "finished_at", "requested_at", "preview_generated_at", "last_attempt_at" }
            }, cancellationToken: ct));

            var map = columns.ToDictionary(x => x.ColumnName, x => x.Exists, StringComparer.OrdinalIgnoreCase);

            var errorColumn = new[] { "error_message", "error", "last_error", "error_text", "preview_error", "message" }.FirstOrDefault(c => map.ContainsKey(c));
            var previewPathColumn = map.ContainsKey("preview_path") ? "preview_path" : null;
            var attemptsColumn = new[] { "attempts", "preview_attempts" }.FirstOrDefault(c => map.ContainsKey(c));
            var lastUpdatedColumn = new[] { "finished_at", "updated_at", "last_attempt_at", "preview_generated_at", "requested_at" }.FirstOrDefault(c => map.ContainsKey(c));
            var lastAttemptColumn = map.ContainsKey("last_attempt_at") ? "last_attempt_at" : null;

            var sql = $@"
SELECT
    tenant_id AS ""TenantId"",
    document_version_id AS ""VersionId"",
    status::text AS ""Status"",
    {(previewPathColumn is null ? "NULL::text" : previewPathColumn)} AS ""PreviewPath"",
    {(errorColumn is null ? "NULL::text" : errorColumn)} AS ""ErrorMessage"",
    {(attemptsColumn is null ? "0" : attemptsColumn)}::int AS ""Attempts"",
    {(lastUpdatedColumn is null ? "NULL::timestamptz" : lastUpdatedColumn)} AS ""LastUpdatedAt"",
    requested_at AS ""RequestedAt"",
    finished_at AS ""FinishedAt"",
    {(lastAttemptColumn is null ? "NULL::timestamptz" : lastAttemptColumn)} AS ""LastAttemptAt""
FROM ged.preview_status
WHERE tenant_id = @tenantId AND document_version_id = @versionId
LIMIT 1;";

            var row = await conn.QuerySingleOrDefaultAsync<PreviewStatusRow>(new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
            if (row is null) return null;

            return new PreviewStatusDto
            {
                TenantId = row.TenantId,
                VersionId = row.VersionId,
                Status = ParseStatus(row.Status),
                PreviewPath = row.PreviewPath,
                ErrorMessage = row.ErrorMessage,
                Attempts = row.Attempts,
                LastUpdatedAt = row.LastUpdatedAt ?? row.FinishedAt ?? row.LastAttemptAt ?? row.RequestedAt,
                RequestedAt = row.RequestedAt,
                FinishedAt = row.FinishedAt,
                LastAttemptAt = row.LastAttemptAt
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar preview status. Tenant={TenantId} Version={VersionId}", tenantId, versionId);
            throw;
        }
    }

    public async Task UpsertAsync(Guid tenantId, Guid versionId, PreviewProcessingStatus status, string? previewPath, string? errorMessage, DateTimeOffset? requestedAt, DateTimeOffset? finishedAt, CancellationToken ct)
    {
        try
        {
            const string sql = """
                INSERT INTO ged.preview_status (tenant_id, document_version_id, status, preview_path, error_message, requested_at, finished_at)
                VALUES (@tenantId, @versionId, @status::ged.preview_processing_status, @previewPath, @errorMessage, @requestedAt, @finishedAt)
                ON CONFLICT (tenant_id, document_version_id)
                DO UPDATE SET status = EXCLUDED.status,
                              preview_path = COALESCE(EXCLUDED.preview_path, ged.preview_status.preview_path),
                              error_message = EXCLUDED.error_message,
                              requested_at = COALESCE(EXCLUDED.requested_at, ged.preview_status.requested_at),
                              finished_at = EXCLUDED.finished_at
                """;
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenantId,
                versionId,
                status = ToDatabaseStatus(status),
                previewPath,
                errorMessage,
                requestedAt,
                finishedAt
            }, cancellationToken: ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha em upsert de preview status. Tenant={TenantId} Version={VersionId}", tenantId, versionId);
            throw;
        }
    }

    private static PreviewProcessingStatus ParseStatus(string? status) =>
        status?.Trim().ToUpperInvariant() switch
        {
            "READY" => PreviewProcessingStatus.Ready,
            "PROCESSING" => PreviewProcessingStatus.Processing,
            "FAILED" or "ERROR" => PreviewProcessingStatus.Error,
            "CANCELED" or "CANCELLED" => PreviewProcessingStatus.Canceled,
            _ => PreviewProcessingStatus.Pending
        };

    private static string ToDatabaseStatus(PreviewProcessingStatus status) => status switch
    {
        PreviewProcessingStatus.Ready => "READY",
        PreviewProcessingStatus.Processing => "PROCESSING",
        PreviewProcessingStatus.Error => "FAILED",
        PreviewProcessingStatus.Canceled => "CANCELED",
        _ => "PENDING"
    };

    private sealed class ColumnMetadataRow
    {
        public string ColumnName { get; set; } = string.Empty;
        public bool Exists { get; set; }
    }

    private sealed class PreviewStatusRow
    {
        public Guid TenantId { get; set; }
        public Guid VersionId { get; set; }
        public string? Status { get; set; }
        public string? PreviewPath { get; set; }
        public string? ErrorMessage { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public DateTimeOffset? RequestedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public DateTimeOffset? LastAttemptAt { get; set; }
    }
}
