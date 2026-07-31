using InovaGed.Application.WorkspaceSearch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]/[action]")]
public sealed class WorkspaceSearchController : Controller
{
    private readonly IWorkspaceSearchService _workspaceSearch;
    private readonly ILogger<WorkspaceSearchController> _logger;

    public WorkspaceSearchController(
        IWorkspaceSearchService workspaceSearch,
        ILogger<WorkspaceSearchController> logger)
    {
        _workspaceSearch = workspaceSearch;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType<WorkspaceSearchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkspaceSearchResponse>> Search(
        [FromQuery] string? q,
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length < 2)
            return Ok(new WorkspaceSearchResponse([], 0, TimeSpan.Zero));

        if (!Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var tenantId) || tenantId == Guid.Empty)
            return Forbid();

        try
        {
            var response = await _workspaceSearch.SearchAsync(
                new WorkspaceSearchRequest(query, Math.Clamp(limit, 1, 20)),
                cancellationToken);
            return Ok(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Global workspace search failed.");
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Não foi possível concluir a busca agora.");
        }
    }
}
