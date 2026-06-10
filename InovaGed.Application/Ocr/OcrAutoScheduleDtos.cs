namespace InovaGed.Application.Ocr;

public sealed class OcrAutoScheduleRunResultDto
{
    public Guid RunId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FinishedAtUtc { get; set; }
    public string Status { get; set; } = "RUNNING";
    public string? Message { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public int CandidatesFound { get; set; }
    public int EnqueuedCount { get; set; }
    public int SkippedAlreadyHasOcr { get; set; }
    public int SkippedPending { get; set; }
    public int SkippedProcessing { get; set; }
    public int SkippedUnsupportedExtension { get; set; }
    public int SkippedNoCurrentVersion { get; set; }
    public int FailedCount { get; set; }
    public List<OcrAutoScheduleItemResultDto> Items { get; set; } = new();

    public int SkippedCount => SkippedAlreadyHasOcr + SkippedPending + SkippedProcessing + SkippedUnsupportedExtension + SkippedNoCurrentVersion;
}

public sealed class OcrAutoScheduleItemResultDto
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? OcrJobId { get; set; }
}

public sealed class OcrAutoScheduleCandidateDto
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? VersionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? StoragePath { get; set; }
    public bool HasOcrText { get; set; }
    public string? LastOcrStatus { get; set; }
    public string Source { get; set; } = "DOCUMENT";
}

public sealed class OcrAutoScheduleDashboardDto
{
    public bool Enabled { get; set; }
    public string RunAt { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public int MaxDocumentsPerRun { get; set; }
    public int BatchSize { get; set; }
    public DateTimeOffset NextRunUtc { get; set; }
    public string NextRunLocal { get; set; } = string.Empty;
    public OcrAutoScheduleRunSummaryDto? LastRun { get; set; }
    public int EligibleDocumentsCount { get; set; }
    public List<OcrAutoScheduleRunSummaryDto> History { get; set; } = new();
}

public sealed class OcrAutoScheduleRunSummaryDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CandidatesFound { get; set; }
    public int EnqueuedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public string? Message { get; set; }
    public string? CorrelationId { get; set; }
}
