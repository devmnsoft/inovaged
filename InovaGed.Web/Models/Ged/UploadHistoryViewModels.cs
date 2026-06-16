namespace InovaGed.Web.Models.Ged;

public sealed class GedUploadHistoryVM
{
    public IReadOnlyList<GedUploadBatchRowVM> Batches { get; set; } = Array.Empty<GedUploadBatchRowVM>();
    public bool CanViewTenantBatches { get; set; }
}

public sealed class GedUploadBatchDetailVM
{
    public GedUploadBatchRowVM Batch { get; set; } = new();
    public IReadOnlyList<GedUploadBatchItemVM> Items { get; set; } = Array.Empty<GedUploadBatchItemVM>();
}

public sealed class GedUploadBatchRowVM
{
    public Guid Id { get; set; }
    public Guid CreatedBy { get; set; }
    public string? UserName { get; set; }
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int SuccessFiles { get; set; }
    public int FailedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public int DuplicateFiles { get; set; }
    public int AbortedFiles { get; set; }
    public long? DurationMs { get; set; }
    public bool HasRetryableItems { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class GedUploadBatchItemVM
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStep { get; set; }
    public bool CanRetry { get; set; }
    public long? ElapsedMs { get; set; }
    public string? CorrelationId { get; set; }
    public string? ProcessingWarning { get; set; }
}
