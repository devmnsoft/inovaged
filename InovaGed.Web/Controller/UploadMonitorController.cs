using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
[Route("Ged/UploadMonitor")]
public sealed class UploadMonitorController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IUploadBatchService _batches;

    public UploadMonitorController(ICurrentUser currentUser, IUploadBatchService batches)
    {
        _currentUser = currentUser;
        _batches = batches;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = await _batches.GetMonitorAsync(_currentUser.TenantId, ct);
        return View("~/Views/Ged/UploadMonitor.cshtml", model);
    }

    [HttpPost("CleanStale")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CleanStale(CancellationToken ct)
    {
        var count = await _batches.MarkStaleReceivingItemsAsErrorAsync(TimeSpan.FromMinutes(10), ct);
        TempData["Success"] = $"{count} item(ns) RECEIVING antigo(s) marcado(s) como erro recuperável.";
        return RedirectToAction(nameof(Index));
    }
}
