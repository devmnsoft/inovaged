using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.GedAccess)]
[Route("Ged/Bulk")]
public sealed class GedBulkActionsController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IGedBulkDocumentActionService _service;

    public GedBulkActionsController(ICurrentUser currentUser, IGedBulkDocumentActionService service)
    {
        _currentUser = currentUser;
        _service = service;
    }

    [HttpPost("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromBody] BulkDocumentActionRequest request, CancellationToken ct)
        => Json(await _service.DeleteAsync(_currentUser.TenantId, _currentUser.UserId, User, request, ct));

    [HttpPost("MarkIncomplete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkIncomplete([FromBody] BulkDocumentActionRequest request, CancellationToken ct)
        => Json(await _service.MarkIncompleteAsync(_currentUser.TenantId, _currentUser.UserId, User, request, ct));

    [HttpPost("MarkComplete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkComplete([FromBody] BulkDocumentActionRequest request, CancellationToken ct)
        => Json(await _service.MarkCompleteAsync(_currentUser.TenantId, _currentUser.UserId, User, request, ct));
}
