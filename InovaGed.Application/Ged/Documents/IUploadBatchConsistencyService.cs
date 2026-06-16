using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Documents;

public sealed class UploadBatchConsistencyResult
{
    public Guid BatchId { get; set; }
    public int TotalFiles { get; set; }
    public int SuccessFiles { get; set; }
    public int FailedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public string Status { get; set; } = "OPEN";
    public DateTimeOffset? FinishedAt { get; set; }
}

public interface IUploadBatchConsistencyService
{
    Task<Result<UploadBatchConsistencyResult>> RecalculateAsync(Guid tenantId, Guid batchId, CancellationToken ct);
}
