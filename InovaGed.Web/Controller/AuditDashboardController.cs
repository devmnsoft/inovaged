using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Web.Security;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.LogsAccess)]
[Route("[controller]")]
public sealed class AuditDashboardController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IAuditSecurityService _auditSecurityService;

    public AuditDashboardController(ICurrentUser currentUser, IAuditSecurityService auditSecurityService)
    {
        _currentUser = currentUser;
        _auditSecurityService = auditSecurityService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = await _auditSecurityService.GetDashboardAsync(_currentUser.TenantId, ct);
        return View(vm);
    }
}
