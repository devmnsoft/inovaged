using InovaGed.Application.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Web.Security;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
public sealed class SystemLogsController : Controller
{
    private readonly ISystemLogQueryService _query;
    public SystemLogsController(ISystemLogQueryService query) => _query = query;

    [HttpGet("/SystemLogs")]
    public async Task<IActionResult> Index([FromQuery] SystemLogFilter filter, CancellationToken ct)
    {
        if (filter.TenantId == Guid.Empty && Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var t)) filter.TenantId = t;
        var data = await _query.SearchAsync(filter, ct);
        ViewBag.Filter = filter;
        return View(data);
    }

    [HttpGet("/SystemLogs/Details/{id}")]
    public async Task<IActionResult> Details(string id, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        var item = await _query.GetDetailsAsync(id, tenantId, ct);
        return item is null ? NotFound() : Json(item);
    }

    [HttpGet("/SystemLogs/ExportCsv")]
    public async Task<IActionResult> ExportCsv([FromQuery] SystemLogFilter filter, CancellationToken ct)
    {
        var bytes = await _query.ExportCsvAsync(filter, ct);
        return File(bytes, "text/csv", $"system-logs-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}
