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
