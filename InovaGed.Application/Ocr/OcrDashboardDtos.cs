namespace InovaGed.Application.Ocr;

public sealed class OcrDashboardFilter
{
    public string? Status { get; set; }
    public Guid? FolderId { get; set; }
    public string? Folder { get; set; }
    public string? Search { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? DocumentType { get; set; }
    public bool OnlyPartial { get; set; }
    public bool OnlyPartialDocuments { get => OnlyPartial; set => OnlyPartial = value; }
    public bool OnlyErrors { get; set; }
    public bool OnlyWithoutOcr { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class OcrDashboardVm
{
    public int WithoutOcrCount { get; set; }
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int CompletedCount { get; set; }
    public int CompletedWithoutTextCount { get; set; }
    public int ErrorCount { get; set; }
    public int PartialOcrCount { get; set; }
    public bool AutoScheduleEnabled { get; set; }
    public string AutoScheduleRunAt { get; set; } = string.Empty;
    public DateTimeOffset? NextAutoRun { get; set; }
    public DateTimeOffset? LastAutoRun { get; set; }
    public int LastAutoRunEnqueuedCount { get; set; }
    public string? AutoScheduleWarning { get; set; }
    public IReadOnlyList<OcrQueueItemVm> Items { get; set; } = Array.Empty<OcrQueueItemVm>();
    public OcrDashboardFilter Filter { get; set; } = new();
}

public sealed class OcrQueueItemVm
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string? JobId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? FolderName { get; set; }
    public string? DocumentType { get; set; }
    public string OcrStatus { get; set; } = "NONE";
    public string OcrStatusText { get; set; } = "Sem OCR";
    public string OcrStatusCss { get; set; } = "bg-secondary";
    public bool HasOcrText { get; set; }
    public bool HasOcrError { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public bool IsPartialDocument { get; set; }
    public int? PartNumber { get; set; }
    public int? TotalParts { get; set; }
    public int? PartsWithOcr { get; set; }
    public string? PartialSummaryText { get; set; }
    public string ActionUrl { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public string? UploadedBy { get; set; }
}

public sealed class OcrJobDetailsVm
{
    public string JobId { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Status { get; set; } = "NONE";
    public string StatusText { get; set; } = "Não executado";
    public string? FileName { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class OcrResolvedStatusVm
{
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Status { get; set; } = "NONE";
    public string StatusText { get; set; } = "Sem OCR";
    public string StatusCss { get; set; } = "bg-secondary";
    public bool HasOcrText { get; set; }
    public bool HasError { get; set; }
    public int? TotalParts { get; set; }
    public int? PartsWithOcr { get; set; }
    public string? PartialSummaryText { get; set; }
}

public interface IOcrDashboardService
{
    Task<OcrDashboardVm> GetDashboardAsync(Guid tenantId, OcrDashboardFilter filter, CancellationToken ct);

    Task<IReadOnlyList<OcrQueueItemVm>> GetQueueAsync(Guid tenantId, OcrDashboardFilter filter, CancellationToken ct);

    Task<OcrJobDetailsVm?> GetJobAsync(Guid tenantId, string jobId, CancellationToken ct);

    Task<string?> GetOcrTextByVersionAsync(Guid tenantId, Guid versionId, CancellationToken ct);
}

public interface IOcrStatusResolver
{
    Task<OcrResolvedStatusVm> ResolveForVersionAsync(Guid tenantId, Guid versionId, CancellationToken ct);

    Task<OcrResolvedStatusVm> ResolveForDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct);

    Task<OcrResolvedStatusVm> ResolveForPartialDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
