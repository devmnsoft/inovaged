using InovaGed.Application.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Web.Security;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.LogsAccess)]
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
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogError(ex,
                "Schema desatualizado ao carregar logs do sistema. SqlState={SqlState} Table={Table} Column={Column} MigrationSugerida={Migration} CorrelationId={CorrelationId}",
                ex.SqlState,
                ex.TableName,
                ex.ColumnName,
                "database/apply_all_required_migrations.sql",
                HttpContext.TraceIdentifier);
            TempData["Error"] = "Estrutura do banco de dados desatualizada. Execute as migrations do sistema.";
            ViewBag.Filter = filter;
            return View(new PagedResult<SystemLogListItemDto>
            {
                Items = Array.Empty<SystemLogListItemDto>(),
                Page = Math.Max(1, filter.Page),
                PageSize = Math.Clamp(filter.PageSize, 1, 200),
                Total = 0
            });
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
