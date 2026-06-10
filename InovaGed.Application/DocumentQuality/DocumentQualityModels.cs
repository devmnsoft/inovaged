namespace InovaGed.Application.DocumentQuality;

public sealed class DocumentQualityFilter
{
    public string? Status { get; set; }
    public int? ScoreMin { get; set; }
    public int? ScoreMax { get; set; }
    public bool? WithoutOcr { get; set; }
    public bool? OcrError { get; set; }
    public bool? WithoutClassification { get; set; }
    public bool? WithoutDocumentType { get; set; }
    public bool? WithoutMetadata { get; set; }
    public bool? PartialDocument { get; set; }
    public bool? ReadyToConsolidate { get; set; }
    public bool? LgpdRisk { get; set; }
    public bool? MissingStorageFile { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? SectorId { get; set; }
    public DateTime? UploadedFrom { get; set; }
    public DateTime? UploadedTo { get; set; }
    public DateTime? AnalyzedFrom { get; set; }
    public DateTime? AnalyzedTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int? MaxDocuments { get; set; }
    public bool AnalyzeStorage { get; set; } = true;
    public bool AnalyzeLgpd { get; set; } = true;
}

public sealed class DocumentQualityResultDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public string? FolderName { get; set; }
    public string? DocumentTypeName { get; set; }
    public int QualityScore { get; set; }
    public string QualityStatus { get; set; } = string.Empty;
    public bool HasOcr { get; set; }
    public bool HasOcrError { get; set; }
    public bool HasClassification { get; set; }
    public bool HasDocumentType { get; set; }
    public bool HasRequiredMetadata { get; set; }
    public bool IsPartialDocument { get; set; }
    public bool IsPartialIncomplete { get; set; }
    public bool IsReadyToConsolidate { get; set; }
    public bool IsConsolidated { get; set; }
    public bool? StorageFileExists { get; set; }
    public bool HasPossibleDuplicate { get; set; }
    public bool HasLgpdRisk { get; set; }
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Recommendations { get; set; } = Array.Empty<string>();
    public string? NextAction { get; set; }
    public DateTime AnalyzedAtUtc { get; set; }
}

public sealed class DocumentQualityDashboardDto
{
    public DocumentQualityFilter Filter { get; set; } = new();
    public double AverageScore { get; set; }
    public int TotalDocuments { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int WarningCount { get; set; }
    public int CriticalCount { get; set; }
    public int WithoutOcrCount { get; set; }
    public int WithoutClassificationCount { get; set; }
    public int IncompleteCount { get; set; }
    public int LgpdRiskCount { get; set; }
    public int MissingStorageCount { get; set; }
    public IReadOnlyList<DocumentQualityResultDto> Items { get; set; } = Array.Empty<DocumentQualityResultDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
}

public sealed class DocumentQualityRunResultDto
{
    public Guid RunId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int WarningCount { get; set; }
    public int CriticalCount { get; set; }
    public int FailedCount { get; set; }
    public string? Message { get; set; }
}

public interface IDocumentQualityAnalyzerService
{
    Task<DocumentQualityRunResultDto> AnalyzeAllAsync(Guid tenantId, DocumentQualityFilter filter, CancellationToken ct);
    Task<DocumentQualityResultDto> AnalyzeOneAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<DocumentQualityDashboardDto> GetDashboardAsync(Guid tenantId, DocumentQualityFilter filter, CancellationToken ct);
    Task<IReadOnlyList<DocumentQualityResultDto>> GetHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}

public sealed class DocumentQualityOptions
{
    public bool Enabled { get; set; } = true;
    public string RunAt { get; set; } = "19:00";
    public string TimeZone { get; set; } = "America/Belem";
    public Guid TenantId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public int MaxDocumentsPerRun { get; set; } = 2000;
    public bool AnalyzeStorage { get; set; } = true;
    public bool AnalyzeLgpd { get; set; } = true;
}
