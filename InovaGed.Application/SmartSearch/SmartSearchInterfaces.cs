namespace InovaGed.Application.SmartSearch;

public interface ISmartSearchService
{
    Task<SmartSearchResult> SearchAsync(SmartSearchRequest request, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, Guid userId, string? term, CancellationToken ct);
}

public interface ISmartQueryParser
{
    Task<SmartSearchIntent> ParseAsync(Guid tenantId, string query, SmartSearchRequest request, CancellationToken ct);
}

public interface ISmartSearchRepository
{
    Task<SmartSearchResult> SearchAsync(SmartSearchIntent intent, UserDocumentScope scope, SmartSearchRequest request, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, Guid userId, string? term, CancellationToken ct);
    Task<string?> GetDocumentOcrAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task LogQueryAsync(SmartSearchRequest request, SmartSearchIntent intent, int resultsCount, long durationMs, CancellationToken ct);
    Task LogAccessAsync(Guid tenantId, Guid userId, Guid documentId, string source, string action, CancellationToken ct);
    Task SaveFeedbackAsync(Guid tenantId, Guid userId, Guid documentId, string conversationId, bool helpful, CancellationToken ct);
    Task SaveConversationTurnAsync(Guid tenantId, Guid userId, string conversationId, string question, DocumentAssistantResponse response, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchConversationSummary>> GetConversationHistoryAsync(Guid tenantId, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchConversationMessage>> GetConversationMessagesAsync(Guid tenantId, Guid userId, Guid conversationId, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchSavedSearch>> GetSavedSearchesAsync(Guid tenantId, Guid userId, CancellationToken ct);
    Task SaveSearchAsync(Guid tenantId, Guid userId, string name, string query, CancellationToken ct);
    Task<bool> RenameSavedSearchAsync(Guid tenantId, Guid userId, Guid id, string name, CancellationToken ct);
    Task<bool> SetSavedSearchFavoriteAsync(Guid tenantId, Guid userId, Guid id, bool isFavorite, CancellationToken ct);
    Task<string?> RunSavedSearchAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct);
    Task<bool> DeleteSavedSearchAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct);
    Task<SmartSearchStatistics> GetStatisticsAsync(Guid tenantId, CancellationToken ct);
    Task<int> ReindexAsync(Guid tenantId, Guid? documentId, CancellationToken ct);
    Task<SmartSearchAdminDashboard> GetAdminDashboardAsync(Guid tenantId, string section, CancellationToken ct);
    Task SaveSynonymAsync(Guid tenantId, Guid? id, string term, string synonym, string category, decimal weight, bool active, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchRelatedDocument>> GetRelatedDocumentsAsync(Guid tenantId, Guid userId, Guid documentId, bool isAdmin, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchComparisonDocument>> CompareDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, bool includeText, bool isAdmin, CancellationToken ct);
    Task<IReadOnlyList<SmartSearchCollection>> GetCollectionsAsync(Guid tenantId, Guid userId, CancellationToken ct);
    Task<SmartSearchCollection?> GetCollectionAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct);
    Task<Guid> CreateCollectionAsync(Guid tenantId, Guid userId, string name, CancellationToken ct);
    Task<bool> RenameCollectionAsync(Guid tenantId, Guid userId, Guid id, string name, CancellationToken ct);
    Task<bool> ArchiveCollectionAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct);
    Task<SmartSearchCollectionMutation> AddCollectionItemsAsync(Guid tenantId, Guid userId, Guid id, IReadOnlyCollection<Guid> documentIds, CancellationToken ct);
    Task<bool> RemoveCollectionItemAsync(Guid tenantId, Guid userId, Guid id, Guid documentId, CancellationToken ct);
}

public sealed class SmartSearchSavedSearch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public int RunCount { get; set; }
    public bool IsFavorite { get; set; }
}

public sealed class SmartSearchRelatedDocument
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public bool IsFormal { get; set; } = true;
}

public sealed class SmartSearchComparisonDocument
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? DocumentType { get; set; }
    public string? Classification { get; set; }
    public string? Unit { get; set; }
    public string? Protocol { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int? VersionNumber { get; set; }
    public string? ExtractedText { get; set; }
    public bool HasExtractedText { get; set; }
}

public sealed class SmartSearchCollection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int ItemCount { get; set; }
    public IReadOnlyList<SmartSearchCollectionItem> Items { get; set; } = [];
}

public sealed class SmartSearchCollectionItem
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string ReferenceMode { get; set; } = "CURRENT";
    public string Title { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}

public sealed class SmartSearchCollectionMutation
{
    public int Requested { get; set; }
    public int Added { get; set; }
    public int Skipped { get; set; }
}

/// <summary>Read model materialized directly by Dapper. Keep setters public and SQL aliases exact.</summary>
public sealed class SmartSearchConversationMessage
{
    public Guid MessageId { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "[]";
    public string FiltersJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
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
