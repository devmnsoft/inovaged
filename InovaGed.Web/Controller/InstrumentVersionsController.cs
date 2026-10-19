using InovaGed.Application.ClassificationPlans;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("Instruments/Versions")]
[Authorize]
public sealed class InstrumentVersionsController : Controller
{
    private readonly InstrumentVersionRepository _repo;
    private readonly IClassificationPlanRepository _plans;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<InstrumentVersionsController> _logger;

    public InstrumentVersionsController(
        InstrumentVersionRepository repo,
        IClassificationPlanRepository plans,
        ICurrentUser currentUser,
        ILogger<InstrumentVersionsController> logger)
    {
        _repo = repo;
        _plans = plans;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{type}")]
    public async Task<IActionResult> Index(string type, CancellationToken ct)
    {
        var normalized = type.ToUpperInvariant();
        var tenantId = _currentUser.TenantId;
        IReadOnlyList<InstrumentVersionRow> rows;
        try
        {
            rows = (await _repo.ListAsync(tenantId, normalized, ct)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Histórico de {Type} indisponível", normalized);
            TempData["Err"] = "O histórico deste instrumento não pôde ser lido.";
            rows = Array.Empty<InstrumentVersionRow>();
        }

        if (normalized is "PCD" or "TTD")
        {
            try
            {
                ViewBag.Classes = await _plans.ListTreeAsync(tenantId, ct);
                ViewBag.PlanVersions = await _plans.ListVersionsAsync(tenantId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Plano vigente indisponível na versão {Type}", normalized);
                ViewBag.Classes = Array.Empty<ClassificationNodeRow>();
                ViewBag.PlanVersions = Array.Empty<ClassificationVersionRow>();
            }
        }

        ViewData["Type"] = normalized;
        return View(rows);
    }

    [HttpPost("{type}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string type, string? notes, CancellationToken ct)
    {
        var normalized = type.ToUpperInvariant();
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        Guid? planVersionId = null;
        try
        {
            if (normalized is "PCD" or "TTD")
            {
                var title = string.IsNullOrWhiteSpace(notes)
                    ? $"{normalized} publicado em {DateTime.Now:dd/MM/yyyy HH:mm}"
                    : notes.Trim();
                planVersionId = await _repo.PublishPlanSnapshotAsync(tenantId, userId, title, notes ?? "", ct);
            }

            try
            {
                await _repo.PublishAsync(tenantId, normalized, userId, notes ?? "", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cabeçalho do instrumento {Type} não foi registrado", normalized);
                if (planVersionId is null) throw;
                TempData["Err"] = "O snapshot do plano foi gravado. O histórico auxiliar do instrumento não pôde ser atualizado.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publicação de {Type} falhou", normalized);
            TempData["Err"] = "A publicação não foi concluída. O plano vigente permanece como está.";
            return RedirectToAction(nameof(Index), new { type = normalized });
        }

        TempData["Ok"] = planVersionId is null
            ? "Versão do instrumento registrada."
            : "Versão publicada. A pré-visualização mostra o nome de cada classe. O título identifica só a versão.";
        if (planVersionId is Guid id)
            return RedirectToAction("PrintVersion", "ClassificationPlan", new { id });
        return RedirectToAction(nameof(Index), new { type = normalized });
    }

    [HttpGet("{type}/diff")]
    public async Task<IActionResult> Diff(string type, Guid from, Guid to, CancellationToken ct)
    {
        var normalized = type.ToUpperInvariant();
        try
        {
            var diff = await _repo.DiffAsync(_currentUser.TenantId, from, to, ct);
            ViewData["Type"] = normalized;
            ViewData["From"] = from;
            ViewData["To"] = to;
            return View(diff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Comparação de {Type} falhou", normalized);
            TempData["Err"] = "A comparação não pôde ser montada.";
            return RedirectToAction(nameof(Index), new { type = normalized });
        }
    }
}
