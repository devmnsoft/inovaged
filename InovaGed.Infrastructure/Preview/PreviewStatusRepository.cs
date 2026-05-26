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
            var map = (await conn.QueryAsync<(string ColumnName, bool Exists)>(new CommandDefinition(@"
SELECT c.column_name AS ColumnName, true AS Exists
FROM information_schema.columns c
WHERE c.table_schema='ged'
  AND c.table_name='preview_status'
  AND c.column_name = ANY(@columns);", new
            {
                columns = new[] { "preview_path", "error_message", "error", "last_error", "error_text", "preview_error", "message", "preview_attempts", "updated_at", "finished_at", "requested_at", "preview_generated_at" }
            }, cancellationToken: ct))).ToDictionary(x => x.ColumnName, x => x.Exists, StringComparer.OrdinalIgnoreCase);

            var errorColumn = new[] { "error_message", "error", "last_error", "error_text", "preview_error", "message" }.FirstOrDefault(c => map.ContainsKey(c));
            var previewPathColumn = map.ContainsKey("preview_path") ? "preview_path" : null;
            var attemptsColumn = map.ContainsKey("preview_attempts") ? "preview_attempts" : null;
            var lastUpdatedColumn = new[] { "updated_at", "finished_at", "preview_generated_at", "requested_at" }.FirstOrDefault(c => map.ContainsKey(c));

            var sql = $@"
SELECT
    tenant_id AS TenantId,
    document_version_id AS VersionId,
    status::text AS Status,
    {(previewPathColumn is null ? "NULL::text" : previewPathColumn)} AS PreviewPath,
    {(errorColumn is null ? "NULL::text" : errorColumn)} AS ErrorMessage,
    {(attemptsColumn is null ? "0" : attemptsColumn)}::int AS Attempts,
    {(lastUpdatedColumn is null ? "NULL::timestamptz" : lastUpdatedColumn)} AS LastUpdatedAt,
    requested_at AS RequestedAt,
    finished_at AS FinishedAt
FROM ged.preview_status
WHERE tenant_id = @tenantId AND document_version_id = @versionId
LIMIT 1;";

            var row = await conn.QuerySingleOrDefaultAsync(sql, new { tenantId, versionId });
            if (row is null) return null;

            return new PreviewStatusDto
            {
                TenantId = row.TenantId,
                VersionId = row.VersionId,
                Status = Enum.TryParse<PreviewProcessingStatus>((string)row.Status, true, out var st) ? st : PreviewProcessingStatus.Pending,
                PreviewPath = row.PreviewPath,
                ErrorMessage = row.ErrorMessage,
                RequestedAt = row.RequestedAt,
                FinishedAt = row.FinishedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar preview status. Tenant={TenantId} Version={VersionId}", tenantId, versionId);
            return new PreviewStatusDto
            {
                TenantId = tenantId,
                VersionId = versionId,
                Status = PreviewProcessingStatus.Error,
                ErrorMessage = "Status do OCR/preview indisponível no momento."
            };
        }
    }

    public async Task UpsertAsync(Guid tenantId, Guid versionId, PreviewProcessingStatus status, string? previewPath, string? errorMessage, DateTimeOffset? requestedAt, DateTimeOffset? finishedAt, CancellationToken ct)
    {
        try
        {
            const string sql = """
                INSERT INTO ged.preview_status (tenant_id, document_version_id, status, preview_path, error_message, requested_at, finished_at)
                VALUES (@tenantId, @versionId, @status, @previewPath, @errorMessage, @requestedAt, @finishedAt)
                ON CONFLICT (tenant_id, document_version_id)
                DO UPDATE SET status = EXCLUDED.status,
                              preview_path = EXCLUDED.preview_path,
                              error_message = EXCLUDED.error_message,
                              requested_at = COALESCE(EXCLUDED.requested_at, ged.preview_status.requested_at),
                              finished_at = EXCLUDED.finished_at
                """;
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenantId,
                versionId,
                status = status.ToString().ToUpperInvariant(),
                previewPath,
                errorMessage,
                requestedAt,
                finishedAt
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha em upsert de preview status. Tenant={TenantId} Version={VersionId}", tenantId, versionId);
            throw;
        }
    }
}
