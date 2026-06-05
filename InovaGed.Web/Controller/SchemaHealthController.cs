using System.Text;
using System.Text.Json;
using InovaGed.Application.SystemHealth;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
[Route("SystemHealth/Schema")]
public sealed class SchemaHealthController : Controller
{
    private readonly ISchemaHealthService _schemaHealth;

    public SchemaHealthController(ISchemaHealthService schemaHealth)
    {
        _schemaHealth = schemaHealth;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var report = await _schemaHealth.CheckAsync(ct);
        return View("~/Views/SystemHealth/Schema.cshtml", report);
    }

    [HttpGet("Data")]
    public async Task<IActionResult> Data(CancellationToken ct)
    {
        var report = await _schemaHealth.CheckAsync(ct);
        return Json(report);
    }

    [HttpGet("Report")]
    public async Task<IActionResult> Report(CancellationToken ct)
    {
        var report = await _schemaHealth.CheckAsync(ct);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"schema-health-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
    }
}
