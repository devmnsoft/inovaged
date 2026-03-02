using InovaGed.Application.ClassificationPlans;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Route("[controller]")]
public sealed class ClassificationPlanController : Controller
{
    private readonly IClassificationPlanRepository _repo;
    private readonly ILogger<ClassificationPlanController> _logger;

    // ✅ AJUSTE para o padrão real do seu projeto
    // Exemplo 1: private readonly ICurrentContext _ctx;  => TenantId => _ctx.TenantId
    // Exemplo 2: private readonly ICurrentUser _currentUser => TenantId => _currentUser.TenantId
    private Guid TenantId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    private Guid UserId => Guid.Empty;

    public ClassificationPlanController(IClassificationPlanRepository repo, ILogger<ClassificationPlanController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            var nodes = await _repo.ListTreeAsync(TenantId, ct);
            return View(nodes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar PCD/TTD");
            TempData["Error"] = "Erro ao carregar o PCD/TTD.";
            return View(Array.Empty<ClassificationNodeRow>());
        }
    }

    [HttpGet("Edit")]
    public async Task<IActionResult> Edit(Guid? id, Guid? parentId, CancellationToken ct)
    {
        try
        {
            if (id is null)
                return PartialView("_EditModal", new ClassificationEditVM { ParentId = parentId });

            var vm = await _repo.GetAsync(TenantId, id.Value, ct);
            if (vm is null) return NotFound();

            return PartialView("_EditModal", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao abrir modal de edição");
            return BadRequest("Erro ao abrir edição.");
        }
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ClassificationEditVM vm, CancellationToken ct)
    {
        try
        {
            await _repo.UpsertAsync(TenantId, UserId, vm, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro Save Classification");
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    public sealed class MoveRequest
    {
        public Guid Id { get; set; }
        public Guid? NewParentId { get; set; }
    }

    [HttpPost("Move")]
    public async Task<IActionResult> Move([FromBody] MoveRequest req, CancellationToken ct)
    {
        try
        {
            await _repo.MoveAsync(TenantId, UserId, req.Id, req.NewParentId, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro Move");
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    [HttpGet("Versions")]
    public async Task<IActionResult> Versions(CancellationToken ct)
    {
        try
        {
            var list = await _repo.ListVersionsAsync(TenantId, ct);
            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar versões");
            TempData["Error"] = "Erro ao carregar versões.";
            return View(Array.Empty<ClassificationVersionRow>());
        }
    }

    [HttpPost("Publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string title, string? notes, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title))
                return BadRequest(new { ok = false, error = "Título obrigatório." });

            await _repo.PublishVersionAsync(TenantId, UserId, title.Trim(), notes, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro Publish");
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    [HttpGet("PrintCurrent")]
    public async Task<IActionResult> PrintCurrent(CancellationToken ct)
    {
        var nodes = await _repo.ListTreeAsync(TenantId, ct);
        return View("PrintCurrent", nodes);
    }

    [HttpGet("PrintVersion")]
    public async Task<IActionResult> PrintVersion(Guid versionId, CancellationToken ct)
    {
        var ver = await _repo.GetVersionAsync(TenantId, versionId, ct);
        if (ver is null) return NotFound();

        var items = await _repo.ListVersionItemsAsync(TenantId, versionId, ct);
        ViewBag.Version = ver;
        return View("PrintVersion", items);
    }
}