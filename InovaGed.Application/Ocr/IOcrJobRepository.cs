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

    Task MarkCompletedAsync(long jobId, CancellationToken ct);
    Task MarkErrorAsync(long jobId, string errorMessage, CancellationToken ct);
    Task RenewLeaseAsync(long id, CancellationToken stoppingToken);
}


public sealed record OcrJobLease(
    long JobId,
    Guid TenantId,
    Guid DocumentVersionId,
    bool InvalidateDigitalSignatures
);
