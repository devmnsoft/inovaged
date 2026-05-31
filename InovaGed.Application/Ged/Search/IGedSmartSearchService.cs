namespace InovaGed.Application.Ged.Search;

public interface IGedSmartSearchService
{
    Task<IReadOnlyList<SmartSearchSuggestionDto>> SuggestAsync(SmartSearchRequest request, CancellationToken ct);
    Task<GedSearchResultDto> SearchAsync(SmartSearchRequest request, CancellationToken ct);
}

public sealed class SmartSearchRequest
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Query { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public string Scope { get; set; } = "folder";
    public string Module { get; set; } = "GED";
    public int Limit { get; set; } = 20;
    public bool IsAdmin { get; set; }
}

public sealed class SmartSearchSuggestionDto
{
    public string Group { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public Guid? FolderId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Snippet { get; set; }
    public string Icon { get; set; } = "bi-file-earmark-text";
    public string? Url { get; set; }
    public decimal Score { get; set; }

    // Compatibilidade com consumidores AJAX legados.
    public Guid? Id => DocumentId ?? FolderId;
}
