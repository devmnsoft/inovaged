using Microsoft.AspNetCore.Mvc;

[Route("Instruments/Versions")]
public sealed class InstrumentVersionsController : Controller
{
    private readonly InstrumentVersionRepository _repo;
    public InstrumentVersionsController(InstrumentVersionRepository repo) => _repo = repo;

    [HttpGet("{type}")]
    public async Task<IActionResult> Index(string type, CancellationToken ct)
    {
        var tenantId = Tenant(); // tua forma padrão
        var rows = await _repo.ListAsync(tenantId, type.ToUpperInvariant(), ct);
        ViewData["Type"] = type.ToUpperInvariant();
        return View(rows);
    }

    [HttpPost("{type}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string type, string? notes, CancellationToken ct)
    {
        var tenantId = Tenant();
        var userId = UserId();
        await _repo.PublishAsync(tenantId, type.ToUpperInvariant(), userId, notes ?? "", ct);
        return RedirectToAction(nameof(Index), new { type });
    }

    [HttpGet("{type}/diff")]
    public async Task<IActionResult> Diff(string type, Guid from, Guid to, CancellationToken ct)
    {
        var tenantId = Tenant();
        var diff = await _repo.DiffAsync(tenantId, from, to, ct);
        ViewData["Type"] = type.ToUpperInvariant();
        ViewData["From"] = from;
        ViewData["To"] = to;
        return View(diff);
    }

    private Guid Tenant() => Guid.Parse("00000000-0000-0000-0000-000000000001");
    private Guid UserId() => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
}