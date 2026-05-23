using InovaGed.Application.Audit;
using InovaGed.Application.Ged.Dashboard;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
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
    public async Task<IActionResult> Index([FromQuery] bool refresh = false, CancellationToken ct = default)
    {
        if (!User.IsInRole(AppRoles.Admin))
        {
            await _auditWriter.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "ACCESS_DENIED", "GED_DASHBOARD", null, "Acesso negado ao Painel GED", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { denied = true }, ct);
            return RedirectToAction("AccessDenied", "Account");
        }

        try
        {
            await _auditWriter.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "GED_DASHBOARD", null, "VIEW_GED_DASHBOARD", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { action = "VIEW_GED_DASHBOARD" }, ct);
            _logger.LogInformation("GED Dashboard opened. Tenant={Tenant} User={User}", _currentUser.TenantId, _currentUser.UserId);

            var vm = await _service.GetAsync(_currentUser.TenantId, _currentUser.UserId, refresh, ct);
            return View(vm ?? new GedDashboardVm { HasPartialFailures = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada ao carregar o GED Dashboard. Tenant={Tenant} User={User}", _currentUser.TenantId, _currentUser.UserId);
            return View(new GedDashboardVm { HasPartialFailures = true });
        }
    }
}
