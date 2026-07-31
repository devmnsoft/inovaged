using System.Diagnostics;
using InovaGed.Application.Common.Context;
using InovaGed.Application.WorkspaceSearch;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.WorkspaceSearch;

public sealed class WorkspaceSearchService : IWorkspaceSearchService
{
    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] = "Documentos",
            ["protocols"] = "Protocolos",
            ["loans"] = "Empréstimos",
            ["users"] = "Usuários"
        };

    private readonly IEnumerable<IWorkspaceSearchProvider> _providers;
    private readonly ICurrentContext _currentContext;
    private readonly ILogger<WorkspaceSearchService> _logger;

    public WorkspaceSearchService(
        IEnumerable<IWorkspaceSearchProvider> providers,
        ICurrentContext currentContext,
        ILogger<WorkspaceSearchService> logger)
    {
        _providers = providers;
        _currentContext = currentContext;
        _logger = logger;
    }

    public async Task<WorkspaceSearchResponse> SearchAsync(
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var query = request.Query?.Trim() ?? string.Empty;
        var maximum = Math.Clamp(request.MaximumResults, 1, 20);
        if (query.Length < 2 || !_currentContext.IsAuthenticated || _currentContext.TenantId == Guid.Empty)
            return new WorkspaceSearchResponse([], 0, timer.Elapsed);

        var context = new WorkspaceSearchContext(
            query,
            _currentContext.TenantId,
            _currentContext.UserId,
            maximum);
        var groups = new List<WorkspaceSearchGroup>();
        var remaining = maximum;

        foreach (var provider in _providers)
        {
            if (remaining == 0) break;
            try
            {
                var items = (await provider.SearchAsync(context, cancellationToken))
                    .Where(IsSafe)
                    .Take(remaining)
                    .ToArray();
                if (items.Length == 0) continue;
                groups.Add(new WorkspaceSearchGroup(
                    provider.Code,
                    Labels.GetValueOrDefault(provider.Code, provider.Code),
                    items));
                remaining -= items.Length;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Workspace search provider {ProviderCode} failed.", provider.Code);
            }
        }

        timer.Stop();
        return new WorkspaceSearchResponse(groups, groups.Sum(group => group.Items.Count), timer.Elapsed);
    }

    private static bool IsSafe(WorkspaceSearchItem item) =>
        Uri.TryCreate(item.Url, UriKind.Relative, out _) &&
        item.Url.StartsWith('/', StringComparison.Ordinal) &&
        !item.Url.StartsWith("//", StringComparison.Ordinal);
}
