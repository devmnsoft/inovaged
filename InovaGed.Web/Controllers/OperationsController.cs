using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Application.Operations;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.Operations)]
[Route("[controller]")]
public sealed class OperationsController : Controller
{
    private readonly IOperationsDashboardService _service;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;

    public OperationsController(IOperationsDashboardService service, ICurrentUser currentUser, IAuditWriter audit)
    {
        _service = service;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return RedirectToAction("AccessDenied", "Account");
        await AuditAsync("OPERATIONS_DASHBOARD_VIEW", "Visualização da Central Operacional", filter, ct);
        var vm = await _service.GetSummaryAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct);
        return View(vm);
    }

    [HttpGet("Summary")]
    public async Task<IActionResult> Summary([FromQuery] OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return Forbid();
        await AuditAsync("OPERATIONS_QUEUE_FILTER", "Filtro da Central Operacional", filter, ct);
        return Json(await _service.GetSummaryAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct));
    }

    [HttpGet("GedQueue")]
    public async Task<IActionResult> GedQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return Forbid();
        await AuditAsync("OPERATIONS_ACTION_OPEN", "Abertura da fila GED", filter, ct);
        return Json(await _service.GetGedQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct));
    }

    [HttpGet("LoanQueue")]
    public async Task<IActionResult> LoanQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return Forbid();
        await AuditAsync("OPERATIONS_ACTION_OPEN", "Abertura da fila de empréstimos", filter, ct);
        return Json(await _service.GetLoanQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct));
    }

    [HttpGet("ProtocolQueue")]
    public async Task<IActionResult> ProtocolQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return Forbid();
        await AuditAsync("OPERATIONS_ACTION_OPEN", "Abertura da fila de protocolos", filter, ct);
        return Json(await _service.GetProtocolQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct));
    }

    [HttpGet("Alerts")]
    public async Task<IActionResult> Alerts([FromQuery] OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return Forbid();
        await AuditAsync("OPERATIONS_ALERT_VIEW", "Visualização de alertas operacionais", filter, ct);
        return Json(await _service.GetAlertsAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct));
    }

    private bool CanAccessOperations()
        => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.AdministradorOphir) || User.IsInRole(AppRoles.ArquivistaOphir);

    private Task AuditAsync(string action, string summary, object data, CancellationToken ct)
        => _audit.WriteAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            action,
            "OPERATIONS_DASHBOARD",
            null,
            summary,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            data,
            ct);
}
