namespace InovaGed.Application.Preview;

public sealed class PreviewStatusDto
{
    public Guid TenantId { get; init; }
    public Guid VersionId { get; init; }
    public PreviewProcessingStatus Status { get; init; }
    public string? PreviewPath { get; init; }
    public string? ErrorMessage { get; init; }
    public int Attempts { get; init; }
    public DateTimeOffset? LastUpdatedAt { get; init; }
    public DateTimeOffset? RequestedAt { get; init; }
    public string RequestedAtKind { get; init; } = "UNSPECIFIED";
    public DateTimeOffset? FinishedAt { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? AttemptStartedAt { get; init; }
    public DateTimeOffset? OutcomeConfirmedAt { get; init; }
    public Guid? CurrentAttemptId { get; init; }
    public string? PersistenceError { get; init; }
    public string? PendingPreviewPath { get; init; }
    public bool CancelRequested { get; init; }
}
