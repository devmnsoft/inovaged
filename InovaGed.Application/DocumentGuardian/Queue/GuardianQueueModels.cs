namespace InovaGed.Application.DocumentGuardian.Queue;

public enum GuardianEvaluationStatus { PENDING, PROCESSING, COMPLETED, FAILED, DEAD_LETTER, CANCELLED }
public sealed record GuardianEvaluationQueueItem(Guid Id, Guid TenantId, Guid DocumentId, string Reason, int Priority, GuardianEvaluationStatus Status, int Attempts, int MaxAttempts, DateTime ScheduledAtUtc, string CorrelationId);

public interface IDocumentGuardianQueue
{
    Task<Guid> EnqueueAsync(Guid tenantId, Guid documentId, string reason, int priority, string correlationId, CancellationToken ct);
    Task<GuardianEvaluationQueueItem?> TryAcquireNextAsync(Guid tenantId, string workerId, DateTime nowUtc, CancellationToken ct);
    Task CompleteAsync(Guid tenantId, Guid queueId, int processedFindings, CancellationToken ct);
    Task FailAsync(Guid tenantId, Guid queueId, string error, DateTime nextAttemptUtc, CancellationToken ct);
}
