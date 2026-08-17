using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using InovaGed.Application.Identity;

[Route("Instruments/Versions")]
[Authorize]
public sealed class InstrumentVersionsController : Controller
{
    private readonly InstrumentVersionRepository _repo;
    private readonly ICurrentUser _currentUser;
    public InstrumentVersionsController(InstrumentVersionRepository repo, ICurrentUser currentUser)
    {
        _repo = repo;
        _currentUser = currentUser;
    }

    [HttpGet("{type}")]
    public async Task<IActionResult> Index(string type, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var rows = await _repo.ListAsync(tenantId, type.ToUpperInvariant(), ct);
        ViewData["Type"] = type.ToUpperInvariant();
        return View(rows);
    }

    [HttpPost("{type}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string type, string? notes, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        await _repo.PublishAsync(tenantId, type.ToUpperInvariant(), userId, notes ?? "", ct);
        return RedirectToAction(nameof(Index), new { type });
    }

    [HttpGet("{type}/diff")]
    public async Task<IActionResult> Diff(string type, Guid from, Guid to, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var diff = await _repo.DiffAsync(tenantId, from, to, ct);
        ViewData["Type"] = type.ToUpperInvariant();
        ViewData["From"] = from;
        ViewData["To"] = to;
        return View(diff);
    }
}
