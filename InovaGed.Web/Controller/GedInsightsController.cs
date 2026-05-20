using InovaGed.Application.Audit;
using InovaGed.Application.Classification;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class GedInsightsController : Controller
{
    private readonly ICurrentUser _user;
    private readonly IAuditSecurityService _audit;
    private readonly IGedIntelligenceService _intelligence;

    public GedInsightsController(ICurrentUser user, IAuditSecurityService audit, IGedIntelligenceService intelligence)
    {
        _user = user;
        _audit = audit;
        _intelligence = intelligence;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var dashboard = await _audit.GetDashboardAsync(_user.TenantId, ct);
        var duplicates = await _intelligence.DetectDuplicatesAsync(_user.TenantId, ct);
        ViewBag.DuplicatesCount = duplicates.Count;
        return View(dashboard);
    }
}
