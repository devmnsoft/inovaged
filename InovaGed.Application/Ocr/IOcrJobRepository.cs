namespace InovaGed.Application.Ocr;

public sealed record OcrJobDto(
    long Id,
    Guid TenantId,
    Guid DocumentVersionId,
    bool InvalidateDigitalSignatures);

public interface IOcrJobRepository
{
    Task<long> EnqueueAsync(
        Guid tenantId,
        Guid documentVersionId,
        Guid? requestedBy,
        bool invalidateDigitalSignatures,
        CancellationToken ct);

    /// <summary>
    /// Pega 1 job pendente e já "trava" para este worker (SKIP LOCKED).
    /// Retorna null se não houver job.
    /// </summary>
    Task<OcrJobDto?> DequeueAndMarkProcessingAsync(CancellationToken ct);

    Task MarkCompletedAsync(long jobId, CancellationToken ct);
    Task MarkErrorAsync(long jobId, string errorMessage, CancellationToken ct);
}
