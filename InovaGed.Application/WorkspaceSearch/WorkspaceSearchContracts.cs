namespace InovaGed.Application.WorkspaceSearch;

public interface IWorkspaceSearchService
{
    Task<WorkspaceSearchResponse> SearchAsync(
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceSearchRequest(string Query, int MaximumResults = 12);

public sealed record WorkspaceSearchResponse(
    IReadOnlyList<WorkspaceSearchGroup> Groups,
    int Total,
    TimeSpan Duration);

public sealed record WorkspaceSearchGroup(
    string Code,
    string Label,
    IReadOnlyList<WorkspaceSearchItem> Items);

public sealed record WorkspaceSearchItem(
    string Type,
    string Title,
    string? Subtitle,
    string Icon,
    string Url,
    string? Badge,
    string? Description);

public sealed record WorkspaceSearchContext(
    string Query,
    Guid TenantId,
    Guid UserId,
    int MaximumResults);

public interface IWorkspaceSearchProvider
{
    string Code { get; }

    Task<IReadOnlyList<WorkspaceSearchItem>> SearchAsync(
        WorkspaceSearchContext context,
        CancellationToken cancellationToken);
}
