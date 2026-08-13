namespace InovaGed.Application.SmartSearch;

/// <summary>Safe, provider-neutral entry point for conversational document discovery.</summary>
public interface IDocumentAssistantService
{
    Task<DocumentAssistantResponse> AskAsync(DocumentAssistantQuery query, CancellationToken ct);
}

public sealed class DocumentAssistantQuery
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public bool IsAdmin { get; set; }
    public string Question { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public Guid? FolderId { get; set; }
    public string? ConversationId { get; set; }
    public IReadOnlyList<DocumentAssistantMessage> History { get; set; } = [];
    public DocumentAssistantSecurityContext? SecurityContext { get; set; }
}

public sealed class DocumentAssistantResponse
{
    public string Answer { get; set; } = string.Empty;
    public string Criteria { get; set; } = string.Empty;
    public IReadOnlyList<DocumentAssistantSource> Sources { get; set; } = [];
    public IReadOnlyList<DocumentAssistantSuggestion> Suggestions { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public bool HasEvidence => Sources.Count > 0;
    public string ConversationId { get; set; } = string.Empty;
    public DocumentAssistantCriteria AppliedCriteria { get; set; } = new();
    public IReadOnlyList<DocumentAssistantMessage> Messages { get; set; } = [];
    public IReadOnlyList<DocumentAssistantAction> Actions { get; set; } = [];
}

public sealed class DocumentAssistantSource
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FolderName { get; set; }
    public string? DocumentType { get; set; }
    public string? OcrExcerpt { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public bool HasOcr { get; set; }
    public decimal Relevance { get; set; }
    public IReadOnlyList<string> Badges { get; set; } = [];
}

public sealed class DocumentAssistantSuggestion
{
    public string Text { get; set; } = string.Empty;
    public string Category { get; set; } = "Descoberta";
}

public sealed class DocumentAssistantConversationState
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString("N");
    public List<string> Questions { get; set; } = [];
}


public sealed class DocumentAssistantMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DocumentAssistantCriteria
{
    public string OriginalQuestion { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool UsedOcr { get; set; }
    public bool UsedMetadata { get; set; }
    public bool IsSensitive { get; set; }
}

public sealed class DocumentAssistantSecurityContext
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public bool CanReadOcr { get; set; } = true;
    public bool CanViewRestrictedDocuments { get; set; }
}

public sealed class DocumentAssistantAction
{
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Value { get; set; }
}
