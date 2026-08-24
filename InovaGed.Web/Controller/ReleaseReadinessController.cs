using InovaGed.Application.SystemHealth.Migrations;
using InovaGed.Web.Models.ReleaseReadiness;
using InovaGed.Web.Security;
using InovaGed.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
[Route("ReleaseReadiness")]
public sealed class ReleaseReadinessController(IReleaseReadinessService readiness, IDatabaseMigrationRunner migrations, ILogger<ReleaseReadinessController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await BuildAsync(ct));

    [HttpGet("Modules")]
    public async Task<IActionResult> Modules(CancellationToken ct) => View(await BuildAsync(ct));

    [HttpGet("Checklist")]
    public async Task<IActionResult> Checklist(CancellationToken ct) => View(await BuildAsync(ct));

    [HttpGet("Smoke")]
    public async Task<IActionResult> Smoke(CancellationToken ct) => View(await BuildAsync(ct));

    [HttpGet("Report")]
    public async Task<IActionResult> Report(CancellationToken ct) => View(await BuildAsync(ct));

    [HttpPost("Refresh"), ValidateAntiForgeryToken]
    public IActionResult Refresh()
    {
        logger.LogInformation("RELEASE_READINESS_REFRESHED CorrelationId={CorrelationId}", HttpContext.TraceIdentifier);
        return RedirectToAction(nameof(Index));
    }

    private async Task<ReleaseReadinessViewModel> BuildAsync(CancellationToken ct)
    {
        var plan = await migrations.GetPlanAsync(ct);
        return new(readiness.GetModules(plan.Pending > 0), plan.Pending, DateTimeOffset.UtcNow);
    }
}
