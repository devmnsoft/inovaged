using InovaGed.Application.WorkspaceSearch;
using InovaGed.Web.Models.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("GlobalSearch")]
public sealed class GlobalSearchController(IWorkspaceSearchService search, ILogger<GlobalSearchController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? q, CancellationToken ct)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length > 100)
            query = query[..100];

        var response = new WorkspaceSearchResponse([], 0, TimeSpan.Zero);
        if (query.Length >= 2)
        {
            try
            {
                response = await search.SearchAsync(new WorkspaceSearchRequest(query, 20), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "GLOBAL_SEARCH_FAILED TenantClaim={TenantClaim}", User.FindFirst("tenant_id")?.Value);
                ModelState.AddModelError(string.Empty, "A busca está temporariamente indisponível. Tente novamente.");
            }
        }

        return View(new GlobalSearchPageVm(query, response));
    }
}
