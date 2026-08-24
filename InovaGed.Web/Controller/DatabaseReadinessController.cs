using System.Text;
using InovaGed.Application.Identity;
using InovaGed.Application.SystemHealth.Migrations;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
[Route("DatabaseReadiness")]
public sealed class DatabaseReadinessController(IDatabaseMigrationRunner runner, ICurrentUser currentUser, ILogger<DatabaseReadinessController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) { logger.LogInformation("DATABASE_MIGRATION_PLAN_VIEWED UserId={UserId} CorrelationId={CorrelationId}", currentUser.UserId, HttpContext.TraceIdentifier); return View(await runner.GetPlanAsync(ct)); }
    [HttpGet("Plan")]
    public async Task<IActionResult> Plan(CancellationToken ct) => View(await runner.GetPlanAsync(ct));
    [HttpGet("Report")]
    public async Task<IActionResult> Report(CancellationToken ct) => View(await runner.GetPlanAsync(ct));

    [HttpPost("ApplyRequired"), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.SchemaRepair)]
    public async Task<IActionResult> ApplyRequired(CancellationToken ct) { var result = await runner.ApplyRequiredAsync(currentUser.UserId, currentUser.Email, ct); logger.Log(result.Success ? LogLevel.Information : LogLevel.Error, result.Success ? "DATABASE_MIGRATION_APPLIED UserId={UserId}" : "DATABASE_MIGRATION_FAILED UserId={UserId} Message={Message}", currentUser.UserId, result.Message); TempData["MigrationResult"] = result.Message; return RedirectToAction(nameof(Report)); }

    [HttpPost("ApplyOne"), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.SchemaRepair)]
    public async Task<IActionResult> ApplyOne(string migrationName, CancellationToken ct) { var result = await runner.ApplyOneAsync(migrationName, currentUser.UserId, currentUser.Email, ct); logger.Log(result.Success ? LogLevel.Information : LogLevel.Error, result.Success ? "DATABASE_MIGRATION_APPLIED Migration={Migration}" : "DATABASE_MIGRATION_FAILED Migration={Migration} Message={Message}", migrationName, result.Message); TempData["MigrationResult"] = result.Message; return RedirectToAction(nameof(Report)); }

    [HttpGet("DownloadConsolidatedScript")]
    public async Task<IActionResult> DownloadConsolidatedScript(CancellationToken ct) { logger.LogInformation("DATABASE_MIGRATION_SCRIPT_DOWNLOADED UserId={UserId}", currentUser.UserId); return File(Encoding.UTF8.GetBytes(await runner.GetConsolidatedPendingScriptAsync(ct)), "text/plain; charset=utf-8", $"inovaged-required-migrations-{DateTime.UtcNow:yyyyMMddHHmmss}.sql"); }
}
