namespace InovaGed.Application.SmartSearch;

public interface ISmartSearchService
{
    Task<SmartSearchResult> SearchAsync(SmartSearchRequest request, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, string? term, CancellationToken ct);
}

public interface ISmartQueryParser
{
    Task<SmartSearchIntent> ParseAsync(Guid tenantId, string query, SmartSearchRequest request, CancellationToken ct);
}

public interface ISmartSearchRepository
{
    Task<SmartSearchResult> SearchAsync(SmartSearchIntent intent, UserDocumentScope scope, SmartSearchRequest request, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, string? term, CancellationToken ct);
    Task<string?> GetDocumentOcrAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task LogQueryAsync(SmartSearchRequest request, SmartSearchIntent intent, int resultsCount, long durationMs, CancellationToken ct);
    Task LogAccessAsync(Guid tenantId, Guid userId, Guid documentId, string source, string action, CancellationToken ct);
    Task<SmartSearchStatistics> GetStatisticsAsync(Guid tenantId, CancellationToken ct);
    Task<int> ReindexAsync(Guid tenantId, Guid? documentId, CancellationToken ct);
}

public interface IDocumentChatService
{
    Task<DocumentQuestionAnswer> AskAsync(Guid tenantId, Guid userId, DocumentQuestionRequest request, CancellationToken ct);
}

public interface ISearchStatisticsService
{
    Task<SmartSearchStatistics> GetAsync(Guid tenantId, CancellationToken ct);
}

public interface IDocumentOcrMetadataExtractor
{
    (int? Age, int? Year, string? PatientName, IReadOnlyList<string> Terms) Extract(string? text);
}
