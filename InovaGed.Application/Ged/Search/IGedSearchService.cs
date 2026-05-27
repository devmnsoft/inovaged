namespace InovaGed.Application.Ged.Search;

public interface IGedSearchService
{
    Task<GedSearchResultDto> SearchAsync(GedSearchFilter filter, CancellationToken ct);
}
