namespace InovaGed.Application.Ged.Search;

public sealed class GedSearchFilter
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string? Term { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public Guid? ClassificationId { get; set; }
    public string? OcrStatus { get; set; }
    public string? DocumentStatus { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public Guid? UploadedBy { get; set; }
    public bool OnlyUnclassified { get; set; }
    public bool OnlyWithSuggestion { get; set; }
    public bool OnlyWithOcr { get; set; }
    public bool OnlyOcrError { get; set; }
    public string? Visibility { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class GedSearchResultDto
{
    public IReadOnlyList<GedSearchResultItemDto> Items { get; set; } = Array.Empty<GedSearchResultItemDto>();
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalPages { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class GedSearchResultItemDto
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }
    public string? FileExtension { get; set; }
    public string? FolderName { get; set; }
    public string? FolderPath { get; set; }
    public Guid? FolderId { get; set; }
    public string? DocumentType { get; set; }
    public string? ClassificationCode { get; set; }
    public string? ClassificationName { get; set; }
    public string? OcrStatus { get; set; }
    public string? DocumentStatus { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
    public bool HasOcr { get; set; }
    public bool HasSuggestion { get; set; }
    public bool IsConfidential { get; set; }
    public string? Visibility { get; set; }
    public string? OcrSnippet { get; set; }
    public decimal Score { get; set; }
    public bool CanView { get; set; }
    public bool CanDownload { get; set; }
    public bool CanClassify { get; set; }
    public bool CanMove { get; set; }
}

public sealed class GedSearchSuggestionDto
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
}
