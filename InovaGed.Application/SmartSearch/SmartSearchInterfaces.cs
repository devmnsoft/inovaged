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
    Task SaveFeedbackAsync(Guid tenantId, Guid userId, Guid documentId, string conversationId, bool helpful, CancellationToken ct);
    Task SaveConversationTurnAsync(Guid tenantId, Guid userId, string conversationId, string question, DocumentAssistantResponse response, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchConversationSummary>> GetConversationHistoryAsync(Guid tenantId, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchSavedSearch>> GetSavedSearchesAsync(Guid tenantId, Guid userId, CancellationToken ct);
    Task SaveSearchAsync(Guid tenantId, Guid userId, string name, string query, CancellationToken ct);
    Task DeleteSavedSearchAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct);
    Task<SmartSearchStatistics> GetStatisticsAsync(Guid tenantId, CancellationToken ct);
    Task<int> ReindexAsync(Guid tenantId, Guid? documentId, CancellationToken ct);
    Task<SmartSearchAdminDashboard> GetAdminDashboardAsync(Guid tenantId, string section, CancellationToken ct);
    Task SaveSynonymAsync(Guid tenantId, Guid? id, string term, string synonym, string category, decimal weight, bool active, CancellationToken ct);
}

public sealed record SmartSearchSavedSearch(Guid Id, string Name, string Query, DateTimeOffset CreatedAt);

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
