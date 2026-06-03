using InovaGed.Application.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Web.Security;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
public sealed class SystemLogsController : Controller
{
    private readonly ISystemLogQueryService _query;
    private readonly ILogger<SystemLogsController> _logger;

    public SystemLogsController(ISystemLogQueryService query, ILogger<SystemLogsController> logger)
    {
        _query = query;
        _logger = logger;
    }

    [HttpGet("/SystemLogs")]
    public async Task<IActionResult> Index([FromQuery] SystemLogFilter filter, CancellationToken ct)
    {
        if (filter.TenantId == Guid.Empty && Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var t)) filter.TenantId = t;

        try
        {
            var data = await _query.SearchAsync(filter, ct);
            ViewBag.Filter = filter;
            return View(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar logs do sistema.");
            TempData["Error"] = "Não foi possível carregar os logs do sistema.";
            ViewBag.Filter = filter;
            return View(new PagedResult<SystemLogListItemDto>
            {
                Items = Array.Empty<SystemLogListItemDto>(),
                Page = Math.Max(1, filter.Page),
                PageSize = Math.Clamp(filter.PageSize, 1, 200),
                Total = 0
            });
        }
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
