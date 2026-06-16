using InovaGed.Domain.Primitives;
using Microsoft.AspNetCore.Http;

namespace InovaGed.Application.Ged.Documents;

public sealed class UploadBatchOptionsDto
{
    public bool RunOcr { get; set; }
    public bool GeneratePreview { get; set; }
    public string? DuplicateStrategy { get; set; }
    public bool MarkAsIncomplete { get; set; }
    public string? IncompleteReason { get; set; }
}

public sealed class StartUploadBatchRequestDto
{
    public Guid? FolderId { get; set; }
    public Guid? RequestedFolderId { get; set; }
    public int TotalFiles { get; set; }
    public UploadBatchOptionsDto? Options { get; set; }
    public string? SourceIp { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class UploadBatchFileRequestDto
{
    public Guid BatchId { get; set; }
    public IFormFile File { get; set; } = default!;
    public int FileIndex { get; set; }
    public int TotalFiles { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? RequestedFolderId { get; set; }
    public string? DuplicateStrategy { get; set; }
    public bool RunOcr { get; set; }
    public bool GeneratePreview { get; set; }
    public string? UploadName { get; set; }
    public bool MarkAsIncomplete { get; set; }
    public string? IncompleteReason { get; set; }
    public string? UploadClientId { get; set; }
    public string? ContentHash { get; set; }
    public Guid? ExistingDocumentId { get; set; }
    public DocumentBulkUploadMetadata Metadata { get; set; } = new();
    public string? UserName { get; set; }
    public bool IsAdmin { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class UploadBatchFileResultDto
{
    public Guid ItemId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public Guid? RequestedFolderId { get; set; }
    public Guid? ResolvedFolderId { get; set; }
    public string? FolderName { get; set; }
    public string? Title { get; set; }
    public string? FileName { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public string? UploadedAtLocalFormatted { get; set; }
    public string Status { get; set; } = "COMPLETED";
    public bool OcrQueued { get; set; }
    public bool PreviewQueued { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? ProcessingWarning { get; set; }
}

public sealed class UploadBatchCreatedDocumentDto
{
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string? Title { get; set; }
    public string? FileName { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public string? UploadedAtLocalFormatted { get; set; }
}

public sealed class UploadBatchStatusDto
{
    public Guid BatchId { get; set; }
    public Guid? RequestedFolderId { get; set; }
    public Guid? ResolvedFolderId { get; set; }
    public string? FolderName { get; set; }
    public IReadOnlyList<UploadBatchCreatedDocumentDto> CreatedDocuments { get; set; } = Array.Empty<UploadBatchCreatedDocumentDto>();
    public string Status { get; set; } = "OPEN";
    public int Total { get; set; }
    public int Success { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Pending { get; set; }
    public IReadOnlyList<UploadBatchItemStatusDto> Items { get; set; } = Array.Empty<UploadBatchItemStatusDto>();
    public IReadOnlyList<UploadBatchItemStatusDto> Errors { get; set; } = Array.Empty<UploadBatchItemStatusDto>();
}

public sealed class UploadBatchItemStatusDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string? StoredFileName { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? ErrorStep { get; set; }
    public bool CanRetry { get; set; }
    public long? SizeBytes { get; set; }
    public string? CorrelationId { get; set; }
    public string? ProcessingWarning { get; set; }
}

public sealed class UploadMonitorDto
{
    public IReadOnlyList<UploadMonitorBatchDto> Batches { get; set; } = Array.Empty<UploadMonitorBatchDto>();
    public IReadOnlyList<UploadBatchItemStatusDto> StaleReceivingItems { get; set; } = Array.Empty<UploadBatchItemStatusDto>();
    public int PendingOcrCount { get; set; }
}

public sealed class UploadMonitorBatchDto
{
    public Guid Id { get; set; }
    public Guid? FolderId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int SuccessFiles { get; set; }
    public int FailedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public string? SourceIp { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public double? AvgElapsedMs { get; set; }
}

public interface IUploadBatchService
{
    Task<Result<Guid>> StartAsync(Guid tenantId, Guid userId, StartUploadBatchRequestDto request, CancellationToken ct);
    Task<Result<UploadBatchFileResultDto>> UploadFileAsync(Guid tenantId, Guid userId, UploadBatchFileRequestDto request, CancellationToken ct);
    Task<Result<UploadBatchStatusDto>> FinishAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);
    Task<Result<UploadBatchStatusDto>> GetStatusAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);
    Task<Result<UploadBatchStatusDto>> RetryFailedAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);
    Task<int> MarkStaleReceivingItemsAsErrorAsync(TimeSpan staleAfter, CancellationToken ct);
    Task<UploadMonitorDto> GetMonitorAsync(Guid tenantId, CancellationToken ct);
}

public sealed class UploadConcurrencyLease : IAsyncDisposable
{
    private readonly Action _release;
    private bool _disposed;
    public UploadConcurrencyLease(Action release) => _release = release;
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _release();
        }
        return ValueTask.CompletedTask;
    }
}

public interface IUploadConcurrencyLimiter
{
    Task<UploadConcurrencyLease?> AcquireAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);
}
