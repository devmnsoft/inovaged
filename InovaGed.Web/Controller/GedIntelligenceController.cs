using InovaGed.Application.Ged.Intelligence;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Ged/Intelligence")]
public sealed class GedIntelligenceController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IGedAdministrativeIntelligenceService _service;

    public GedIntelligenceController(ICurrentUser currentUser, IGedAdministrativeIntelligenceService service)
    {
        _currentUser = currentUser;
        _service = service;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return RedirectToAction("Login", "Account");
        if (!RolePolicyHelper.IsFullAdmin(User) && !User.IsInNormalizedRole(AppRoles.Administrador)) return Forbid();
        var vm = await _service.GetAsync(_currentUser.TenantId, ct);
        return View("~/Views/GedIntelligence/Index.cshtml", vm);
    }
}
