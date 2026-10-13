namespace InovaGed.Application.Ged.Documents;

public sealed class GedProcessingJobDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? DocumentVersionId { get; set; }
    public Guid? UploadBatchId { get; set; }
    public Guid? UploadBatchItemId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public interface IGedProcessingJobRepository
{
    Task EnqueueAsync(Guid tenantId, Guid? documentId, Guid? documentVersionId, Guid? uploadBatchId, Guid? uploadBatchItemId, string jobType, int priority, CancellationToken ct);
    Task<IReadOnlyList<GedProcessingJobDto>> DequeueAsync(string workerId, int take, CancellationToken ct);
    Task<IReadOnlyList<GedProcessingJobDto>> DequeueAsync(string workerId, int take, string? jobType, CancellationToken ct);
    Task<int> RecoverAbandonedAsync(string jobType, TimeSpan lease, CancellationToken ct);
    Task<IReadOnlyList<GedProcessingJobDto>> ExpirePendingAsync(string jobType, TimeSpan maxAge, CancellationToken ct);
    Task TouchLockAsync(Guid tenantId, Guid jobId, string workerId, CancellationToken ct);
    Task ReleaseAsync(Guid tenantId, Guid jobId, string workerId, CancellationToken ct);
    Task CompleteAsync(Guid tenantId, Guid jobId, CancellationToken ct);
    Task FailAsync(Guid tenantId, Guid jobId, string errorMessage, TimeSpan retryDelay, CancellationToken ct);
    Task CancelAsync(Guid tenantId, Guid jobId, string reason, CancellationToken ct);
}
