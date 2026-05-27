namespace InovaGed.Application.Ged.Search;

public interface IGedSearchService
{
    Task<GedSearchResultDto> SearchAsync(GedSearchFilter filter, CancellationToken ct);
    Task<IReadOnlyList<GedSearchSuggestionDto>> SuggestAsync(Guid tenantId, Guid userId, string? term, CancellationToken ct);
}
