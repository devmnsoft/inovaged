namespace InovaGed.Application.SmartGed;

public interface IDocumentIntelligenceService
{
    Task<Guid> AnalyzeDocumentAsync(Guid tenantId, Guid documentId, Guid? userId, CancellationToken ct);
    Task<DocumentIntelligenceDetails?> GetAnalysisAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
public interface IDocumentMetadataExtractor { Task<DocumentMetadataExtractionResult> ExtractAsync(DocumentMetadataExtractionInput input, CancellationToken ct); }
public interface IDocumentClassificationSuggestionService
{
    Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListPendingAsync(Guid tenantId, CancellationToken ct) => ListPendingAsync(tenantId, null, ct);
    Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListPendingAsync(Guid tenantId, Guid? userId, CancellationToken ct);
    Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListAsync(Guid tenantId, string? status, CancellationToken ct) => ListAsync(tenantId, status, null, ct);
    Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListAsync(Guid tenantId, string? status, Guid? userId, CancellationToken ct);
    Task AcceptAsync(Guid tenantId, Guid suggestionId, Guid userId, string? notes, CancellationToken ct);
    Task RejectAsync(Guid tenantId, Guid suggestionId, Guid userId, string reason, CancellationToken ct);
}
public interface IDocumentRetentionSuggestionService
{
    Task<IReadOnlyList<DocumentRetentionSuggestionItem>> ListPendingAsync(Guid tenantId, CancellationToken ct) => ListPendingAsync(tenantId, null, ct);
    Task<IReadOnlyList<DocumentRetentionSuggestionItem>> ListPendingAsync(Guid tenantId, Guid? userId, CancellationToken ct);
    Task<IReadOnlyList<DocumentRetentionSuggestionItem>> ListAsync(Guid tenantId, string? status, CancellationToken ct) => ListAsync(tenantId, status, null, ct);
    Task<IReadOnlyList<DocumentRetentionSuggestionItem>> ListAsync(Guid tenantId, string? status, Guid? userId, CancellationToken ct);
    Task AcceptAsync(Guid tenantId, Guid suggestionId, Guid userId, string? notes, CancellationToken ct);
    Task RejectAsync(Guid tenantId, Guid suggestionId, Guid userId, string reason, CancellationToken ct);
}
public interface ISmartGedSearchService { Task<SmartGedSearchResult> SearchAsync(SmartGedSearchQuery query, CancellationToken ct); }

public sealed record DocumentMetadataExtractionInput(string Text, string? FileName = null);
public sealed record DocumentMetadataExtractionResult(string Summary, string? DocumentType, string? Subject, DateOnly? DetectedDate, IReadOnlyList<string> Keywords, IReadOnlyDictionary<string, IReadOnlyList<string>> Identifiers, IReadOnlyList<string> SensitiveIndicators, decimal Confidence);
public sealed record DocumentClassificationSuggestionItem(
    Guid Id,
    Guid DocumentId,
    string? Code,
    string? Title,
    string? Reason,
    decimal Confidence,
    string Status,
    string? DocumentTitle = null,
    int? VersionNumber = null,
    string? CurrentClassification = null,
    DateTimeOffset? CreatedAt = null,
    bool IsObsolete = false,
    string? ReviewNotes = null,
    Guid? VersionId = null,
    Guid? PlanVersionId = null,
    bool IsLegacyUnproven = false);

public sealed record DocumentRetentionSuggestionItem(
    Guid Id,
    Guid DocumentId,
    string? Phase,
    string? FinalDestination,
    string? TriggerEvent,
    DateOnly? RetentionUntil,
    string? Reason,
    decimal Confidence,
    string Status,
    string? DocumentTitle = null,
    int? VersionNumber = null,
    string? CurrentRetention = null,
    DateTimeOffset? CreatedAt = null,
    bool IsObsolete = false,
    string? ReviewNotes = null,
    Guid? VersionId = null,
    Guid? PlanVersionId = null,
    bool IsLegacyUnproven = false);

public sealed record DocumentQualityIssueItem(Guid Id, Guid DocumentId, string Type, string Severity, string Title, string? RecommendedAction, string Status);
public sealed record DocumentIntelligenceDetails(Guid AnalysisId, Guid DocumentId, string Status, string? Summary, string? DocumentType, string? Subject, DateOnly? DetectedDate, IReadOnlyDictionary<string, IReadOnlyList<string>> MaskedIdentifiers, IReadOnlyList<string> SensitiveIndicators, decimal Confidence, DocumentClassificationSuggestionItem? Classification, DocumentRetentionSuggestionItem? Retention, IReadOnlyList<DocumentQualityIssueItem> Issues);
public sealed record SmartGedSearchQuery(Guid TenantId, Guid? UserId, string Text, int Limit = 50, int Page = 1, Guid? FolderId = null, Guid? ClassificationId = null, Guid? TypeId = null, DateTime? CreatedAfter = null, DateTime? CreatedBefore = null, string? RetentionStatus = null);
public sealed record SmartGedSearchItem(Guid DocumentId, string Document, string? Summary, string? Classification, string? PhysicalLocation, string QualityStatus, string Excerpt, int? VersionNumber = null, string? RetentionStatus = null, bool IsAnalysisOutdated = false, bool IsLegacyUnproven = false, Guid? AnalyzedVersionId = null, Guid? CurrentVersionId = null);
public sealed record SmartGedSearchResult(string Query, IReadOnlyList<SmartGedSearchItem> Items, int ExecutionMs, int TotalCount = 0, int Page = 1, int PageSize = 50);
public sealed record SmartGedDashboard(int WithoutOcr, int WithoutClassification, int Sensitive, int LowConfidence, int PendingSuggestions, int OpenIssues);
public sealed record SmartGedReviewQueue(IReadOnlyList<DocumentClassificationSuggestionItem> Classifications, IReadOnlyList<DocumentRetentionSuggestionItem> Retentions, string CurrentStatus = "PENDING");
