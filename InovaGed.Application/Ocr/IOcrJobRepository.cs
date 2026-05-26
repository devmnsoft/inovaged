namespace InovaGed.Application.Ocr;

public interface IOcrJobRepository
{
    Task<long> EnqueueAsync(
        Guid tenantId,
        Guid documentVersionId,
        Guid? requestedBy,
        bool invalidateDigitalSignatures,
        CancellationToken ct);

    Task<OcrJobDto?> DequeueAndMarkProcessingAsync(CancellationToken ct);

    Task<OcrJobLease?> LeaseNextAsync(TimeSpan leaseTime, CancellationToken ct);

    Task MarkCompletedAsync(long jobId, Guid? documentId, Guid? resultVersionId, CancellationToken ct);

    Task MarkCompletedAsync(long jobId, CancellationToken ct);

    Task MarkErrorAsync(long jobId, string errorMessage, CancellationToken ct);

    Task MarkRetryAsync(long jobId, int attempts, DateTimeOffset nextAttemptAt, string errorMessage, CancellationToken ct);

    Task<bool> IsCancelRequestedAsync(long jobId, CancellationToken ct);

    Task RenewLeaseAsync(long id, CancellationToken stoppingToken);

    Task<OcrJobStatusDto?> GetLatestByVersionIdAsync(Guid tenantId, Guid documentVersionId, CancellationToken ct);

    Task<bool> HasCompletedAsync(Guid tenantId, Guid documentVersionId, CancellationToken ct);

    Task<int> CancelByVersionAsync(Guid tenantId, Guid versionId, Guid? cancelledBy, string reason, CancellationToken ct);

    Task<int> CancelQueueAsync(Guid tenantId, Guid? documentId, Guid? cancelledBy, string reason, CancellationToken ct);
}

public sealed record OcrJobLease(
    long JobId,
    Guid TenantId,
    Guid DocumentVersionId,
    bool InvalidateDigitalSignatures
);
