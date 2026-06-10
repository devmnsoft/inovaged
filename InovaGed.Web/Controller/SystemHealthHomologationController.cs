using InovaGed.Application.SystemHealth;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
[Route("SystemHealth/Homologation")]
public sealed class SystemHealthHomologationController : Controller
{
    private readonly IHomologationHealthService _homologation;

    public SystemHealthHomologationController(IHomologationHealthService homologation)
    {
        _homologation = homologation;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var report = await _homologation.GenerateAsync(User?.Identity?.Name, ct);
        return View("~/Views/SystemHealth/Homologation.cshtml", report);
    }

    [HttpGet("Report")]
    public async Task<IActionResult> Report(CancellationToken ct)
    {
        var report = await _homologation.GenerateAsync(User?.Identity?.Name, ct);
        return View("~/Views/SystemHealth/HomologationReport.cshtml", report);
    }
}
