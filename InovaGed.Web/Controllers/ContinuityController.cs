using InovaGed.Application.Continuity;
using InovaGed.Application.Readiness;
using InovaGed.Web.Security;
using InovaGed.Web.Services;
using InovaGed.Web.Models.Continuity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.ContinuityView)]
[Route("Continuity")]
public sealed class ContinuityController(IRecoveryObjectiveService dashboard, IBackupPolicyService policies, IBackupCatalogService catalog, IRecoveryPlanService plans, IPortabilityExportService exports, ITenantOffboardingService offboarding, IBackupOrchestrator orchestrator, IBackupIntegrityService integrity, IAdministrativeTenantScopeResolver tenantScope, IUiModuleAvailabilityService availability, IModuleReadinessService readiness) : Controller
{
    [HttpGet("")] [HttpGet("Overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var unavailable = await UnavailableAsync(false, ct);
        if (unavailable is not null) return unavailable;
        var module = await availability.GetContinuityAsync(ct);
        return View(new ContinuityOverviewVM(module, await dashboard.GetDashboardAsync(ResolveTenant(null), ct)));
    }
    [HttpGet("Backups")] public async Task<IActionResult> Backups(string? status, CancellationToken ct) { var unavailable = await UnavailableAsync(false, ct); return unavailable ?? View(await catalog.ListAsync(ResolveTenant(null), status, ct)); }
    [HttpPost("Backups/Request")] [ValidateAntiForgeryToken] public async Task<IActionResult> RequestBackup(CancellationToken ct){ var unavailable = await UnavailableAsync(true, ct); if (unavailable is not null) return unavailable; await orchestrator.EnqueueBackupAsync(ResolveTenant(null),null,User.Identity?.Name??"admin",HttpContext.TraceIdentifier,ct); return RedirectToAction(nameof(Backups)); }
    [HttpPost("Backups/{id:guid}/Verify")] [ValidateAntiForgeryToken] public async Task<IActionResult> Verify(Guid id, CancellationToken ct){ var unavailable = await UnavailableAsync(true, ct); if (unavailable is not null) return unavailable; await integrity.VerifyAsync(id, Environment.MachineName, ct); return RedirectToAction(nameof(Backups)); }
    [HttpGet("Policies")] public async Task<IActionResult> Policies(CancellationToken ct) { var unavailable = await UnavailableAsync(false, ct); return unavailable ?? View(await policies.ListAsync(ResolveTenant(null), ct)); }
    [HttpGet("RestoreTests")] public async Task<IActionResult> RestoreTests(CancellationToken ct) => await UnavailableAsync(false, ct) ?? View();
    [HttpGet("DisasterRecovery")] public async Task<IActionResult> DisasterRecovery(CancellationToken ct) { var unavailable = await UnavailableAsync(false, ct); return unavailable ?? View(await plans.ListAsync(ResolveTenant(null), ct)); }
    [HttpGet("Portability")] public async Task<IActionResult> Portability(CancellationToken ct) => await UnavailableAsync(false, ct) ?? View();
    [HttpPost("Portability/Request")] [ValidateAntiForgeryToken] public async Task<IActionResult> RequestExport(string scope, CancellationToken ct){ var unavailable = await UnavailableAsync(true, ct); if (unavailable is not null) return unavailable; await exports.RequestAsync(ResolveTenant(null),string.IsNullOrWhiteSpace(scope)?"TENANT":scope,User.Identity?.Name??"admin",Guid.NewGuid().ToString("N"),HttpContext.TraceIdentifier,ct); return RedirectToAction(nameof(Reports)); }
    [HttpGet("Offboarding")] public async Task<IActionResult> Offboarding(CancellationToken ct) { var unavailable = await UnavailableAsync(false, ct); return unavailable ?? View(await offboarding.ListAsync(ResolveTenant(null), ct)); }
    [HttpGet("Reports")] public async Task<IActionResult> Reports(CancellationToken ct) { var unavailable = await UnavailableAsync(false, ct); return unavailable ?? View(await dashboard.GetDashboardAsync(ResolveTenant(null), ct)); }

    private async Task<IActionResult?> UnavailableAsync(bool post, CancellationToken ct)
    {
        var result = await readiness.GetAsync("Continuity", ct);
        if (result.Available) return null;
        if (!post) return View("ModuleUnavailable", result);
        return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Módulo de continuidade indisponível", Detail = "O schema de continuidade ainda não está pronto.", Extensions = { ["reasonCode"] = "CONTINUITY_SCHEMA_NOT_READY", ["correlationId"] = HttpContext.TraceIdentifier } });
    }

    private Guid? ResolveTenant(Guid? requestedTenantId)
    {
        var scope = tenantScope.Resolve(User, requestedTenantId);
        if (!scope.Allowed) return null;
        return scope.TenantId;
    }
}
