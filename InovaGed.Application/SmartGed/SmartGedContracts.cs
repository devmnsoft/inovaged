namespace InovaGed.Application.SmartGed;

public interface IDocumentIntelligenceService
{
    Task<Guid> AnalyzeDocumentAsync(Guid tenantId, Guid documentId, Guid? userId, CancellationToken ct);
    Task<DocumentIntelligenceDetails?> GetAnalysisAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
public interface IDocumentMetadataExtractor { Task<DocumentMetadataExtractionResult> ExtractAsync(DocumentMetadataExtractionInput input, CancellationToken ct); }
public interface IDocumentClassificationSuggestionService
{
    Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListPendingAsync(Guid tenantId, CancellationToken ct);
    Task AcceptAsync(Guid tenantId, Guid suggestionId, Guid userId, string? notes, CancellationToken ct);
    Task RejectAsync(Guid tenantId, Guid suggestionId, Guid userId, string reason, CancellationToken ct);
}
public interface IDocumentRetentionSuggestionService
{
    Task<IReadOnlyList<DocumentRetentionSuggestionItem>> ListPendingAsync(Guid tenantId, CancellationToken ct);
    Task AcceptAsync(Guid tenantId, Guid suggestionId, Guid userId, string? notes, CancellationToken ct);
    Task RejectAsync(Guid tenantId, Guid suggestionId, Guid userId, string reason, CancellationToken ct);
}
public interface ISmartGedSearchService { Task<SmartGedSearchResult> SearchAsync(SmartGedSearchQuery query, CancellationToken ct); }

public sealed record DocumentMetadataExtractionInput(string Text, string? FileName = null);
public sealed record DocumentMetadataExtractionResult(string Summary, string? DocumentType, string? Subject, DateOnly? DetectedDate, IReadOnlyList<string> Keywords, IReadOnlyDictionary<string, IReadOnlyList<string>> Identifiers, IReadOnlyList<string> SensitiveIndicators, decimal Confidence);
public sealed record DocumentClassificationSuggestionItem(Guid Id, Guid DocumentId, string? Code, string? Title, string? Reason, decimal Confidence, string Status);
public sealed record DocumentRetentionSuggestionItem(Guid Id, Guid DocumentId, string? Phase, string? FinalDestination, string? TriggerEvent, DateOnly? RetentionUntil, string? Reason, decimal Confidence, string Status);
public sealed record DocumentQualityIssueItem(Guid Id, Guid DocumentId, string Type, string Severity, string Title, string? RecommendedAction, string Status);
public sealed record DocumentIntelligenceDetails(Guid AnalysisId, Guid DocumentId, string Status, string? Summary, string? DocumentType, string? Subject, DateOnly? DetectedDate, IReadOnlyDictionary<string, IReadOnlyList<string>> MaskedIdentifiers, IReadOnlyList<string> SensitiveIndicators, decimal Confidence, DocumentClassificationSuggestionItem? Classification, DocumentRetentionSuggestionItem? Retention, IReadOnlyList<DocumentQualityIssueItem> Issues);
public sealed record SmartGedSearchQuery(Guid TenantId, Guid? UserId, string Text, int Limit = 50);
public sealed record SmartGedSearchItem(Guid DocumentId, string Document, string? Summary, string? Classification, string? PhysicalLocation, string QualityStatus, string Excerpt);
public sealed record SmartGedSearchResult(string Query, IReadOnlyList<SmartGedSearchItem> Items, int ExecutionMs);
public sealed record SmartGedDashboard(int WithoutOcr, int WithoutClassification, int Sensitive, int LowConfidence, int PendingSuggestions, int OpenIssues);
public sealed record SmartGedReviewQueue(IReadOnlyList<DocumentClassificationSuggestionItem> Classifications, IReadOnlyList<DocumentRetentionSuggestionItem> Retentions);
