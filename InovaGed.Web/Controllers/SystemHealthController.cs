using System.Text;
using System.Text.Json;
using InovaGed.Application.SystemHealth;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
public sealed class SystemHealthController : Controller
{
    private readonly ISchemaHealthService _schemaHealth;

    public SystemHealthController(ISchemaHealthService schemaHealth)
    {
        _schemaHealth = schemaHealth;
    }

    [HttpGet("/SystemHealth/Schema")]
    public async Task<IActionResult> Schema(CancellationToken ct)
    {
        var report = await _schemaHealth.CheckAsync(ct);
        return View(report);
    }

    [HttpGet("/SystemHealth/Schema/Report")]
    public async Task<IActionResult> SchemaReport(CancellationToken ct)
    {
        var report = await _schemaHealth.CheckAsync(ct);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"schema-health-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
    }
}
