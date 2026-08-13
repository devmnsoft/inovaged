using InovaGed.Application.HospitalBilling;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.HospitalDocumentsAccess)]
[Route("HospitalBilling")]
public sealed class HospitalBillingController(ICurrentUser user, IHospitalBillingQueries queries) : Controller
{
    [HttpGet("")][HttpGet("Dashboard")]
    public async Task<IActionResult> Index(string? insurer, string? competence, string? status, bool? hasDenial, CancellationToken ct)
    { var filter = new HospitalBillingFilter(insurer, competence, status, hasDenial); ViewBag.Filter = filter; return View(await queries.DashboardAsync(user.TenantId, filter, ct)); }
    [HttpGet("Documents")] public Task<IActionResult> Documents(string? insurer, string? competence, string? status, bool? hasDenial, CancellationToken ct) => Index(insurer, competence, status, hasDenial, ct);
    [HttpGet("Review")][HttpGet("Glosas")] public Task<IActionResult> WorkQueue(CancellationToken ct) => Index(null, null, Request.Path.Value!.EndsWith("Glosas", StringComparison.OrdinalIgnoreCase) ? null : "PENDING_REVIEW", Request.Path.Value!.EndsWith("Glosas", StringComparison.OrdinalIgnoreCase) ? true : null, ct);
    [HttpGet("Details/{id:guid}")] public async Task<IActionResult> Details(Guid id, CancellationToken ct) => await queries.GetAsync(user.TenantId, id, ct) is { } item ? View(item) : NotFound();
}
