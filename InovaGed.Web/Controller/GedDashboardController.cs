using InovaGed.Application.Audit;
using InovaGed.Application.Ged.Dashboard;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("[controller]")]
public sealed class GedDashboardController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IGedDashboardService _service;
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<GedDashboardController> _logger;

    public GedDashboardController(ICurrentUser currentUser, IGedDashboardService service, IAuditWriter auditWriter, ILogger<GedDashboardController> logger)
    { _currentUser = currentUser; _service = service; _auditWriter = auditWriter; _logger = logger; }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        if (!User.IsInRole(AppRoles.Admin))
        {
            await _auditWriter.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "ACCESS_DENIED", "GED_DASHBOARD", null, "Tentativa de acesso ao Painel Operacional do GED", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { denied = true }, ct);
            return RedirectToAction("AccessDenied", "Account");
        }

        try
        {
            await _auditWriter.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW_GED_DASHBOARD", "GED_DASHBOARD", null, "Visualização do Painel Operacional do GED", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { action = "VIEW_GED_DASHBOARD" }, ct);
            var vm = await _service.GetAsync(_currentUser.TenantId, _currentUser.UserId, ct);
            return View(vm ?? new GedDashboardVm { PartialFailure = true, WarningMessages = ["Falha ao montar o dashboard."] });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha geral do dashboard. Tenant={TenantId} User={UserId}", _currentUser.TenantId, _currentUser.UserId);
            return View(new GedDashboardVm { PartialFailure = true, WarningMessages = ["Falha geral ao carregar o dashboard."] });
        }
    }
}
