using InovaGed.Application.SmartSearch;
using InovaGed.Application.WorkspaceSearch;

namespace InovaGed.Infrastructure.WorkspaceSearch;

public sealed class DocumentWorkspaceSearchProvider : IWorkspaceSearchProvider
{
    private readonly ISmartSearchService _smartSearch;
    public DocumentWorkspaceSearchProvider(ISmartSearchService smartSearch) => _smartSearch = smartSearch;
    public string Code => "documents";

    public async Task<IReadOnlyList<WorkspaceSearchItem>> SearchAsync(
        WorkspaceSearchContext context,
        CancellationToken cancellationToken)
    {
        var result = await _smartSearch.SearchAsync(new SmartSearchRequest
        {
            TenantId = context.TenantId,
            UserId = context.UserId,
            Query = context.Query,
            Page = 1,
            PageSize = Math.Min(5, context.MaximumResults),
            IncludeOcr = true,
            IncludeMetadata = true,
            IncludeStatistics = false,
            IsAdmin = false,
            Source = "WORKSPACE_SEARCH"
        }, cancellationToken);

        return result.Items.Take(5).Select(item => new WorkspaceSearchItem(
            "document",
            item.Title,
            item.DocumentType ?? item.FolderName,
            "bi-file-earmark-text",
            $"/Ged/Details/{item.DocumentId:D}",
            item.HasOcr ? "OCR" : null,
            Truncate(item.OcrSnippet, 160))).ToArray();
    }

    private static string? Truncate(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maximum ? normalized : $"{normalized[..maximum]}…";
    }
}

// Navigation is resolved from the already-authorized shell in the browser. The remaining
// providers are explicit extension points and intentionally return no data until their
// respective authorized application queries are available.
public sealed class NavigationWorkspaceSearchProvider : EmptyWorkspaceSearchProvider { public override string Code => "navigation"; }
public sealed class ProtocolWorkspaceSearchProvider : EmptyWorkspaceSearchProvider { public override string Code => "protocols"; }
public sealed class LoanWorkspaceSearchProvider : EmptyWorkspaceSearchProvider { public override string Code => "loans"; }
public sealed class UserWorkspaceSearchProvider : EmptyWorkspaceSearchProvider { public override string Code => "users"; }

public abstract class EmptyWorkspaceSearchProvider : IWorkspaceSearchProvider
{
    public abstract string Code { get; }
    public Task<IReadOnlyList<WorkspaceSearchItem>> SearchAsync(
        WorkspaceSearchContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkspaceSearchItem>>([]);
}
