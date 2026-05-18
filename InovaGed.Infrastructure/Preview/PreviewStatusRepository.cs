using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Preview;

namespace InovaGed.Infrastructure.Preview;

public sealed class PreviewStatusRepository : IPreviewStatusRepository
{
    private readonly IDbConnectionFactory _db;
    public PreviewStatusRepository(IDbConnectionFactory db) => _db = db;

    public async Task<PreviewStatusDto?> GetAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        const string sql = """
            SELECT tenant_id AS TenantId, document_version_id AS VersionId, status AS Status,
                   preview_path AS PreviewPath, error_message AS ErrorMessage,
                   requested_at AS RequestedAt, finished_at AS FinishedAt
            FROM ged.preview_status
            WHERE tenant_id = @tenantId AND document_version_id = @versionId
            """;
        await using var conn = await _db.OpenAsync(ct);
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

    public async Task UpsertAsync(Guid tenantId, Guid versionId, PreviewProcessingStatus status, string? previewPath, string? errorMessage, DateTimeOffset? requestedAt, DateTimeOffset? finishedAt, CancellationToken ct)
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
}
