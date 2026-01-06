using InovaGed.Application.Documents.Workflow;
using InovaGed.Application.Identity;
using InovaGed.Domain.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Ged/Workflow")]
public sealed class DocumentWorkflowController : Controller
{
    private readonly ICurrentUser _currentUser; // seu contexto atual
    private readonly IDocumentWorkflowService _svc;

    public DocumentWorkflowController(ICurrentUser currentUser, IDocumentWorkflowService svc)
    {
        _currentUser = currentUser;
        _svc = svc;
    }

    [HttpPost("ChangeStatus")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(Guid documentId, DocumentStatus toStatus, string? reason, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var req = new ChangeStatusRequest
        {
            DocumentId = documentId,
            ToStatus = toStatus,
            Reason = reason,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString()
        };

        await _svc.ChangeStatusAsync(tenantId, userId, req, ct);

        // volta para details
        return RedirectToAction("Details", "Ged", new { id = documentId });
    }
}
