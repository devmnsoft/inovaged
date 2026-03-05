using InovaGed.Application.Ged.Physical;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class PhysicalController : Controller
{
    private readonly ILogger<PhysicalController> _logger;
    private readonly ICurrentUser _user;
    private readonly IPhysicalQueries _queries;
    private readonly IPhysicalCommands _commands;

    public PhysicalController(
        ILogger<PhysicalController> logger,
        ICurrentUser user,
        IPhysicalQueries queries,
        IPhysicalCommands commands)
    {
        _logger = logger;
        _user = user;
        _queries = queries;
        _commands = commands;
    }

    // ─────────────── Locations ────────────────────────────────────────────────

    [HttpGet("Locations")]
    public async Task<IActionResult> Locations(string? q, CancellationToken ct)
    {
        try
        {
            var list = await _queries.ListLocationsAsync(_user.TenantId, q, ct);
            ViewBag.Q = q;
            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.Locations failed");
            return View(Array.Empty<PhysicalLocationRowDto>());
        }
    }

    [HttpGet("Locations/New")]
    public IActionResult NewLocation() => View("LocationForm", new PhysicalLocationFormVM());

    [HttpGet("Locations/{id:guid}")]
    public async Task<IActionResult> EditLocation(Guid id, CancellationToken ct)
    {
        try
        {
            var vm = await _queries.GetLocationAsync(_user.TenantId, id, ct);
            if (vm is null) return NotFound();
            return View("LocationForm", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.EditLocation failed");
            return StatusCode(500);
        }
    }

    [HttpPost("Locations/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLocation(PhysicalLocationFormVM vm, CancellationToken ct)
    {
        try
        {
            var res = await _commands.UpsertLocationAsync(_user.TenantId, _user.UserId, vm, ct);
            if (!res.IsSuccess)
            {
                TempData["Err"] = res.ErrorMessage;
                return View("LocationForm", vm);
            }
            TempData["Ok"] = "Localização salva.";
            return RedirectToAction(nameof(Locations));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.SaveLocation failed");
            TempData["Err"] = "Erro ao salvar localização.";
            return View("LocationForm", vm);
        }
    }

    [HttpPost("Locations/{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLocation(Guid id, CancellationToken ct)
    {
        try
        {
            var res = await _commands.DeleteLocationAsync(_user.TenantId, id, _user.UserId, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Localização removida." : res.ErrorMessage;
            return RedirectToAction(nameof(Locations));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.DeleteLocation failed");
            TempData["Err"] = "Erro ao excluir localização.";
            return RedirectToAction(nameof(Locations));
        }
    }

    // ─────────────── Boxes ────────────────────────────────────────────────────

    [HttpGet("Boxes")]
    public async Task<IActionResult> Boxes(string? q, CancellationToken ct)
    {
        try
        {
            var list = await _queries.ListBoxesAsync(_user.TenantId, q, ct);
            ViewBag.Q = q;
            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.Boxes failed");
            return View(Array.Empty<BoxRowDto>());
        }
    }

    // FIX: NewBox agora popula ViewBag.Locations para o select da view
    [HttpGet("Boxes/New")]
    public async Task<IActionResult> NewBox(CancellationToken ct)
    {
        ViewBag.Locations = await _queries.ListLocationsAsync(_user.TenantId, null, ct);
        return View("BoxForm", new BoxFormVM());
    }

    // FIX: EditBox agora popula ViewBag.Locations
    [HttpGet("Boxes/{id:guid}")]
    public async Task<IActionResult> EditBox(Guid id, CancellationToken ct)
    {
        try
        {
            var vm = await _queries.GetBoxAsync(_user.TenantId, id, ct);
            if (vm is null) return NotFound();
            ViewBag.Locations = await _queries.ListLocationsAsync(_user.TenantId, null, ct);
            return View("BoxForm", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.EditBox failed");
            return StatusCode(500);
        }
    }

    [HttpPost("Boxes/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBox(BoxFormVM vm, CancellationToken ct)
    {
        try
        {
            var res = await _commands.UpsertBoxAsync(_user.TenantId, _user.UserId, vm, ct);
            if (!res.IsSuccess)
            {
                TempData["Err"] = res.ErrorMessage;
                ViewBag.Locations = await _queries.ListLocationsAsync(_user.TenantId, null, ct);
                return View("BoxForm", vm);
            }
            TempData["Ok"] = "Caixa salva.";
            return RedirectToAction(nameof(Boxes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.SaveBox failed");
            TempData["Err"] = "Erro ao salvar caixa.";
            ViewBag.Locations = await _queries.ListLocationsAsync(_user.TenantId, null, ct);
            return View("BoxForm", vm);
        }
    }

    [HttpPost("Boxes/{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBox(Guid id, CancellationToken ct)
    {
        try
        {
            var res = await _commands.DeleteBoxAsync(_user.TenantId, id, _user.UserId, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Caixa removida." : res.ErrorMessage;
            return RedirectToAction(nameof(Boxes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.DeleteBox failed");
            TempData["Err"] = "Erro ao excluir caixa.";
            return RedirectToAction(nameof(Boxes));
        }
    }

    // ─────────────── BoxContents (Item 17) ────────────────────────────────────
    // FIX: action criada — antes retornava 404

    [HttpGet("BoxContents")]
    public async Task<IActionResult> BoxContents(Guid? boxId, CancellationToken ct)
    {
        var tenantId = _user.TenantId;
        var boxes = await _queries.ListBoxesAsync(tenantId, null, ct);
        ViewBag.Boxes = boxes;
        ViewBag.SelectedBoxId = boxId;

        if (boxId is null || boxId == Guid.Empty)
        {
            ViewData["Title"] = "Conteúdo da Caixa";
            ViewData["Subtitle"] = "Selecione uma caixa para ver os documentos armazenados.";
            return View(Array.Empty<BoxContentItemDto>());
        }

        try
        {
            var contents = await _queries.GetBoxContentsAsync(tenantId, boxId.Value, ct);
            var selectedBox = boxes.FirstOrDefault(b => b.Id == boxId);
            var boxLabel = selectedBox is not null
                ? $"Caixa #{selectedBox.BoxNo} — {selectedBox.LabelCode}"
                : boxId.ToString();

            ViewData["Title"] = "Conteúdo da Caixa";
            ViewData["Subtitle"] = $"{boxLabel} · {contents.Count} documento(s)";

            return View(contents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.BoxContents failed. Box={BoxId}", boxId);
            TempData["Err"] = "Erro ao carregar conteúdo da caixa.";
            return View(Array.Empty<BoxContentItemDto>());
        }
    }

    // ─────────────── BoxHistory (Item 18/24) ──────────────────────────────────
    // FIX: usa _user.TenantId em vez de HttpContext.Items["TenantId"]

    [HttpGet("BoxHistory")]
    public async Task<IActionResult> BoxHistory(Guid? boxId, CancellationToken ct)
    {
        var tenantId = _user.TenantId;
        var boxes = await _queries.ListBoxesAsync(tenantId, null, ct);
        ViewBag.Boxes = boxes;
        ViewBag.SelectedBoxId = boxId;

        if (boxId is null || boxId == Guid.Empty)
        {
            ViewData["Title"] = "Histórico da Caixa";
            ViewData["Subtitle"] = "Selecione uma caixa para ver o histórico de documentos e fases.";
            return View(Array.Empty<BoxHistoryRowDto>());
        }

        try
        {
            var rows = await _queries.GetBoxHistoryAsync(tenantId, boxId.Value, ct);
            var selectedBox = boxes.FirstOrDefault(b => b.Id == boxId);
            var boxLabel = selectedBox is not null
                ? $"Caixa #{selectedBox.BoxNo} — {selectedBox.LabelCode}"
                : boxId.ToString();

            ViewData["Title"] = "Histórico da Caixa";
            ViewData["Subtitle"] = $"Rastreio físico — {boxLabel}";

            return View(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.BoxHistory failed. Box={BoxId}", boxId);
            TempData["Err"] = "Erro ao carregar histórico.";
            return View(Array.Empty<BoxHistoryRowDto>());
        }
    }
}