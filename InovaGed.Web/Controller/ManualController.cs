using InovaGed.Application.Security;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ManualController : Controller
{
    private readonly ILogger<ManualController> _logger;
    private readonly IGedAccessPolicyService _accessPolicy;

    public ManualController(ILogger<ManualController> logger, IGedAccessPolicyService accessPolicy)
    {
        _logger = logger;
        _accessPolicy = accessPolicy;
    }

    [HttpGet("/Manual")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var tid) ? tid : Guid.Empty;
        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : Guid.Empty;
        if (tenantId != Guid.Empty && userId != Guid.Empty && !await _accessPolicy.CanAccessManualAsync(tenantId, userId, User, ct))
        {
            _logger.LogWarning("Acesso ao manual bloqueado. User={User}", User?.Identity?.Name ?? "anonymous");
            return RedirectToAction("AccessDenied", "Account");
        }
        ViewData["Title"] = "Manual Operacional — InovaGED";
        ViewData["Subtitle"] = "Manual completo de operação do sistema: fluxos, responsabilidades, boas práticas e trilhas de auditoria";
        return View();
    }
}
