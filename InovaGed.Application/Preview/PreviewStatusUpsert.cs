namespace InovaGed.Application.Preview;

public sealed class PreviewStatusUpsert
{
    public required Guid TenantId { get; init; }
    public required Guid VersionId { get; init; }
    public required PreviewProcessingStatus Status { get; init; }
    public string? PreviewPath { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? RequestedAt { get; init; }
    public string? RequestedAtKind { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public Guid? AttemptId { get; init; }
    public string? PersistenceError { get; init; }
    public string? PendingPreviewPath { get; init; }
    public bool ConfirmOutcome { get; init; }
}
