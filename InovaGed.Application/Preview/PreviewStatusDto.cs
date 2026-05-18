namespace InovaGed.Application.Preview;

public sealed class PreviewStatusDto
{
    public Guid TenantId { get; init; }
    public Guid VersionId { get; init; }
    public PreviewProcessingStatus Status { get; init; }
    public string? PreviewPath { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? RequestedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
}
