namespace InovaGed.Application.HospitalAnalytics;

public sealed class HospitalOcrAnalyticsFilter
{
    public Guid TenantId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? FolderId { get; set; }
    public string? Sector { get; set; }
    public string? DocumentType { get; set; }
    public string? Search { get; set; }
    public int Top { get; set; }
    public bool RefreshCache { get; set; }
}

public sealed class HospitalOcrAnalyticsSnapshotDto
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public int TotalDocuments { get; set; }
    public int DocumentsWithOcr { get; set; }
    public int DocumentsWithoutOcr { get; set; }
    public int OcrPending { get; set; }
    public int OcrProcessing { get; set; }
    public int OcrCompleted { get; set; }
    public int OcrErrors { get; set; }
    public int OcrCancelled { get; set; }
    public int UnclassifiedDocuments { get; set; }
    public int ClassifiedDocuments { get; set; }
    public List<OcrDocumentTextRowDto> Rows { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class OcrDocumentTextRowDto
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Folder { get; set; }
    public string? Sector { get; set; }
    public string? DocumentType { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? OcrStatus { get; set; }
    public string TextPreview { get; set; } = string.Empty;
    public bool IsClassified { get; set; }
}

public sealed class TermDictionaryItemDto
{
    public string Term { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string[] Synonyms { get; set; } = [];
}

public sealed class TermMatchDto
{
    public string Term { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public int Occurrences { get; set; }
    public int DocumentCount { get; set; }
    public List<DocumentSnippetDto> Examples { get; set; } = [];
}

public sealed class MoneySignalDto
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string MoneyText { get; set; } = string.Empty;
    public decimal? ParsedValue { get; set; }
    public string Snippet { get; set; } = string.Empty;
}

public sealed class DocumentSnippetDto
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}
