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

    [HttpGet("Locations")]
    public async Task<IActionResult> Locations(string? q, CancellationToken ct)
    {
        var list = await _queries.ListLocationsAsync(_user.TenantId, q, ct);
        ViewBag.Q = q;
        return View(list);
    }

    [HttpGet("Locations/New")]
    public IActionResult NewLocation()
        => View("LocationForm", new PhysicalLocationFormVM());

    [HttpGet("Locations/{id:guid}")]
    public async Task<IActionResult> EditLocation(Guid id, CancellationToken ct)
    {
        var vm = await _queries.GetLocationAsync(_user.TenantId, id, ct);
        if (vm is null) return NotFound();
        return View("LocationForm", vm);
    }

    [HttpPost("Locations/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLocation(PhysicalLocationFormVM vm, CancellationToken ct)
    {
        var res = await _commands.UpsertLocationAsync(_user.TenantId, _user.UserId, vm, ct);

        if (!res.IsSuccess)
        {
            TempData["Err"] = res.ErrorMessage;
            return View("LocationForm", vm);
        }

        TempData["Ok"] = "Localização salva com sucesso.";
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost("Locations/{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLocation(Guid id, CancellationToken ct)
    {
        var res = await _commands.DeleteLocationAsync(_user.TenantId, id, _user.UserId, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Localização removida." : res.ErrorMessage;
        return RedirectToAction(nameof(Locations));
    }

    [HttpGet("Boxes")]
    public async Task<IActionResult> Boxes(string? q, CancellationToken ct)
    {
        var list = await _queries.ListBoxesAsync(_user.TenantId, q, ct);
        ViewBag.Q = q;
        return View(list);
    }

    [HttpGet("Boxes/New")]
    public async Task<IActionResult> NewBox(CancellationToken ct)
    {
        ViewBag.Locations = await _queries.ListLocationsAsync(_user.TenantId, null, ct);
        return View("BoxForm", new BoxFormVM());
    }

    [HttpGet("Boxes/{id:guid}")]
    public async Task<IActionResult> EditBox(Guid id, CancellationToken ct)
    {
        var vm = await _queries.GetBoxAsync(_user.TenantId, id, ct);
        if (vm is null) return NotFound();

        ViewBag.Locations = await _queries.ListLocationsAsync(_user.TenantId, null, ct);
        return View("BoxForm", vm);
    }

    [HttpPost("Boxes/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBox(BoxFormVM vm, CancellationToken ct)
    {
        var res = await _commands.UpsertBoxAsync(_user.TenantId, _user.UserId, vm, ct);

        if (!res.IsSuccess)
        {
            TempData["Err"] = res.ErrorMessage;
            ViewBag.Locations = await _queries.ListLocationsAsync(_user.TenantId, null, ct);
            return View("BoxForm", vm);
        }

        TempData["Ok"] = "Caixa salva com sucesso.";
        return RedirectToAction(nameof(Boxes));
    }

    [HttpPost("Boxes/{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBox(Guid id, CancellationToken ct)
    {
        var res = await _commands.DeleteBoxAsync(_user.TenantId, id, _user.UserId, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Caixa removida." : res.ErrorMessage;
        return RedirectToAction(nameof(Boxes));
    }

    [HttpGet("BoxContents")]
    public async Task<IActionResult> BoxContents(Guid? boxId, string? q, CancellationToken ct)
    {
        var tenantId = _user.TenantId;

        var boxes = await _queries.ListBoxesAsync(tenantId, null, ct);
        ViewBag.Boxes = boxes;
        ViewBag.SelectedBoxId = boxId;
        ViewBag.Q = q;

        if (boxId is null || boxId == Guid.Empty)
        {
            ViewBag.AvailableDocuments = Array.Empty<AvailableDocumentForBoxDto>();
            ViewData["Title"] = "Conteúdo da Caixa";
            ViewData["Subtitle"] = "Selecione uma caixa para ver os documentos armazenados.";
            return View(Array.Empty<BoxContentItemDto>());
        }

        var contents = await _queries.GetBoxContentsAsync(tenantId, boxId.Value, ct);
        var available = await _queries.ListDocumentsAvailableForBoxAsync(tenantId, boxId.Value, q, ct);

        ViewBag.AvailableDocuments = available;

        var selectedBox = boxes.FirstOrDefault(b => b.Id == boxId);
        var boxLabel = selectedBox is not null
            ? $"Caixa #{selectedBox.BoxNo} — {selectedBox.LabelCode}"
            : boxId.ToString();

        ViewData["Title"] = "Conteúdo da Caixa";
        ViewData["Subtitle"] = $"{boxLabel} · {contents.Count} documento(s)";

        return View(contents);
    }

    [HttpPost("BoxContents/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDocumentToBox(BoxContentMaintenanceVM vm, CancellationToken ct)
    {
        var res = await _commands.AddDocumentToBoxAsync(_user.TenantId, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Documento incluído na caixa." : res.ErrorMessage;
        return RedirectToAction(nameof(BoxContents), new { boxId = vm.BoxId });
    }

    [HttpPost("BoxContents/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDocumentFromBox(BoxContentMaintenanceVM vm, CancellationToken ct)
    {
        var res = await _commands.RemoveDocumentFromBoxAsync(_user.TenantId, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Documento removido da caixa." : res.ErrorMessage;
        return RedirectToAction(nameof(BoxContents), new { boxId = vm.BoxId });
    }

    [HttpPost("BoxContents/Move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveDocumentToBox(BoxContentMaintenanceVM vm, CancellationToken ct)
    {
        var res = await _commands.MoveDocumentToBoxAsync(_user.TenantId, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Documento movimentado para a caixa." : res.ErrorMessage;
        return RedirectToAction(nameof(BoxContents), new { boxId = vm.BoxId });
    }

    [HttpGet("BoxHistory")]
    public async Task<IActionResult> BoxHistory(Guid? boxId, CancellationToken ct)
    {
        var tenantId = _user.TenantId;

        var boxes = await _queries.ListBoxesAsync(tenantId, null, ct);
        ViewBag.Boxes = boxes;
        ViewBag.SelectedBoxId = boxId;

        if (boxId is null || boxId == Guid.Empty)
        {
            ViewBag.LocationHistory = Array.Empty<BoxLocationHistoryRowDto>();
            ViewData["Title"] = "Histórico da Caixa";
            ViewData["Subtitle"] = "Selecione uma caixa para ver o histórico físico.";
            return View(Array.Empty<BoxHistoryRowDto>());
        }

        var rows = await _queries.GetBoxHistoryAsync(tenantId, boxId.Value, ct);
        ViewBag.LocationHistory = await _queries.GetBoxLocationHistoryAsync(tenantId, boxId.Value, ct);

        var selectedBox = boxes.FirstOrDefault(b => b.Id == boxId);
        var boxLabel = selectedBox is not null
            ? $"Caixa #{selectedBox.BoxNo} — {selectedBox.LabelCode}"
            : boxId.ToString();

        ViewData["Title"] = "Histórico da Caixa";
        ViewData["Subtitle"] = $"Rastreabilidade física — {boxLabel}";

        return View(rows);
    }

    [HttpGet("PhysicalMap")]
    public async Task<IActionResult> PhysicalMap(string? q, CancellationToken ct)
    {
        ViewBag.Q = q;
        ViewData["Title"] = "Mapa de Guarda Física";
        ViewData["Subtitle"] = "Localização física dos documentos por caixa, lote e endereço de armazenamento.";

        var rows = await _queries.GetPhysicalMapAsync(_user.TenantId, q, ct);
        return View(rows);
    }
}