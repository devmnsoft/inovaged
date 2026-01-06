using InovaGed.Application.Identity;
using InovaGed.Application.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Ged/Workflow")]
public sealed class GedWorkflowController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentWorkflowQueries _queries;

    public GedWorkflowController(ICurrentUser currentUser, IDocumentWorkflowQueries queries)
    {
        _currentUser = currentUser;
        _queries = queries;
    }

    [HttpGet("History")]
    public async Task<IActionResult> History(Guid documentId, int take = 20, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var rows = await _queries.ListAsync(_currentUser.TenantId, documentId, take, ct);
        return PartialView("_WorkflowHistory", rows);
    }
}
