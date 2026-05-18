namespace InovaGed.Application.Preview;

public interface IPreviewStatusRepository
{
    Task<PreviewStatusDto?> GetAsync(Guid tenantId, Guid versionId, CancellationToken ct);
    Task UpsertAsync(Guid tenantId, Guid versionId, PreviewProcessingStatus status, string? previewPath, string? errorMessage, DateTimeOffset? requestedAt, DateTimeOffset? finishedAt, CancellationToken ct);
}
