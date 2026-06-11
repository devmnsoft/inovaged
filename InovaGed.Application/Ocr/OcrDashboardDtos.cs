namespace InovaGed.Application.Ocr;

public sealed class OcrDashboardFilter
{
    public string? Status { get; set; }
    public string? Folder { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? DocumentType { get; set; }
    public bool OnlyPartialDocuments { get; set; }
    public bool OnlyErrors { get; set; }
    public bool OnlyWithoutOcr { get; set; }
}

public sealed class OcrDashboardVm
{
    public int WithoutOcrCount { get; set; }
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int CompletedCount { get; set; }
    public int ErrorCount { get; set; }
    public bool AutoScheduleEnabled { get; set; }
    public string AutoScheduleRunAt { get; set; } = string.Empty;
    public DateTimeOffset? NextAutoRun { get; set; }
    public OcrDashboardFilter Filter { get; set; } = new();
    public IReadOnlyList<OcrQueueItemVm> Items { get; set; } = Array.Empty<OcrQueueItemVm>();
}

public sealed class OcrQueueItemVm
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public Guid? JobId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? FolderName { get; set; }
    public string OcrStatus { get; set; } = "NONE";
    public string OcrStatusText { get; set; } = "Sem OCR";
    public string OcrStatusCss { get; set; } = "bg-secondary";
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsPartialDocument { get; set; }
    public int? PartNumber { get; set; }
    public int? TotalParts { get; set; }
    public bool HasOcrText { get; set; }
    public string ActionUrl { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public long? SizeBytes { get; set; }
    public string? UploadedBy { get; set; }
}

public interface IOcrDashboardService
{
    Task<OcrDashboardVm> GetDashboardAsync(
        Guid tenantId,
        OcrDashboardFilter filter,
        CancellationToken ct);

    Task<IReadOnlyList<OcrQueueItemVm>> GetQueueAsync(
        Guid tenantId,
        OcrDashboardFilter filter,
        CancellationToken ct);
}
