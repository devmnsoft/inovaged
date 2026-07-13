using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.GedAccess)]
[Route("DocumentGuardian")]
public sealed class DocumentGuardianController : Controller
{
    private readonly IDocumentGuardianService _guardian;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DocumentGuardianController> _logger;

    public DocumentGuardianController(IDocumentGuardianService guardian, ICurrentUser currentUser, ILogger<DocumentGuardianController> logger)
    {
        _guardian = guardian;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{documentId:guid}")]
    public async Task<IActionResult> Details(Guid documentId, CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var model = await _guardian.GetAsync(_currentUser.TenantId, _currentUser.UserId, documentId, correlationId, ct);
        if (model is null)
        {
            _logger.LogWarning("DocumentGuardian document not found. Tenant={TenantId} User={UserId} Document={DocumentId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, documentId, correlationId);
            return NotFound("Documento não encontrado ou indisponível para o tenant atual.");
        }

        return View("~/Views/DocumentGuardian/Details.cshtml", model);
    }
}
