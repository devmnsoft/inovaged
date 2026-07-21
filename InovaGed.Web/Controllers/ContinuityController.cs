using InovaGed.Application.Continuity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.ContinuityView)]
[Route("Continuity")]
public sealed class ContinuityController(IRecoveryObjectiveService dashboard, IBackupPolicyService policies, IBackupCatalogService catalog, IRecoveryPlanService plans, IPortabilityExportService exports, ITenantOffboardingService offboarding, IBackupOrchestrator orchestrator, IBackupIntegrityService integrity, IAdministrativeTenantScopeResolver tenantScope) : Controller
{
    [HttpGet("")] [HttpGet("Overview")] public async Task<IActionResult> Overview(CancellationToken ct) => View(await dashboard.GetDashboardAsync(ResolveTenant(null), ct));
    [HttpGet("Backups")] public async Task<IActionResult> Backups(string? status, CancellationToken ct) => View(await catalog.ListAsync(ResolveTenant(null), status, ct));
    [HttpPost("Backups/Request")] [ValidateAntiForgeryToken] public async Task<IActionResult> RequestBackup(CancellationToken ct){ await orchestrator.EnqueueBackupAsync(ResolveTenant(null),null,User.Identity?.Name??"admin",HttpContext.TraceIdentifier,ct); return RedirectToAction(nameof(Backups)); }
    [HttpPost("Backups/{id:guid}/Verify")] [ValidateAntiForgeryToken] public async Task<IActionResult> Verify(Guid id, CancellationToken ct){ await integrity.VerifyAsync(id, Environment.MachineName, ct); return RedirectToAction(nameof(Backups)); }
    [HttpGet("Policies")] public async Task<IActionResult> Policies(CancellationToken ct) => View(await policies.ListAsync(ResolveTenant(null), ct));
    [HttpGet("RestoreTests")] public IActionResult RestoreTests() => View();
    [HttpGet("DisasterRecovery")] public async Task<IActionResult> DisasterRecovery(CancellationToken ct) => View(await plans.ListAsync(ResolveTenant(null), ct));
    [HttpGet("Portability")] public IActionResult Portability() => View();
    [HttpPost("Portability/Request")] [ValidateAntiForgeryToken] public async Task<IActionResult> RequestExport(string scope, CancellationToken ct){ await exports.RequestAsync(ResolveTenant(null),string.IsNullOrWhiteSpace(scope)?"TENANT":scope,User.Identity?.Name??"admin",Guid.NewGuid().ToString("N"),HttpContext.TraceIdentifier,ct); return RedirectToAction(nameof(Reports)); }
    [HttpGet("Offboarding")] public async Task<IActionResult> Offboarding(CancellationToken ct) => View(await offboarding.ListAsync(ResolveTenant(null), ct));
    [HttpGet("Reports")] public async Task<IActionResult> Reports(CancellationToken ct) => View(await dashboard.GetDashboardAsync(ResolveTenant(null), ct));

    private Guid? ResolveTenant(Guid? requestedTenantId)
    {
        var scope = tenantScope.Resolve(User, requestedTenantId);
        if (!scope.Allowed) return null;
        return scope.TenantId;
    }
}
