using InovaGed.Application.Ged.Search;
using InovaGed.Application.SmartSearch;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class GedSearchSuggestionService : IGedSearchSuggestionService
{
    private readonly InovaGed.Application.Ged.Search.IGedSmartSearchService _inner;
    public GedSearchSuggestionService(InovaGed.Application.Ged.Search.IGedSmartSearchService inner) => _inner = inner;
    public Task<IReadOnlyList<InovaGed.Application.Ged.Search.SmartSearchSuggestionDto>> SuggestAsync(InovaGed.Application.Ged.Search.SmartSearchRequest request, CancellationToken ct) => _inner.SuggestAsync(request, ct);
}

public sealed class GedSearchStatisticsService : IGedSearchStatisticsService
{
    private readonly ISearchStatisticsService _inner;
    public GedSearchStatisticsService(ISearchStatisticsService inner) => _inner = inner;
    public Task<SmartSearchStatistics> GetAsync(Guid tenantId, CancellationToken ct) => _inner.GetAsync(tenantId, ct);
}

public sealed class GedSearchIndexService : IGedSearchIndexService
{
    private readonly ISmartSearchRepository _repository;
    public GedSearchIndexService(ISmartSearchRepository repository) => _repository = repository;
    public Task<int> ReindexDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct) => _repository.ReindexAsync(tenantId, documentId, ct);
    public Task<int> ReindexVersionAsync(Guid tenantId, Guid documentVersionId, CancellationToken ct) => _repository.ReindexAsync(tenantId, documentVersionId, ct);
    public Task<int> ReindexPendingAsync(Guid tenantId, int limit, CancellationToken ct) => _repository.ReindexAsync(tenantId, null, ct);
    public Task<int> ReindexAllAsync(Guid tenantId, CancellationToken ct) => _repository.ReindexAsync(tenantId, null, ct);
}
