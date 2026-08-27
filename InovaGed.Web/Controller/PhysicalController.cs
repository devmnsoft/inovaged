using InovaGed.Application.Ged.Physical;
using InovaGed.Application.Identity;
using InovaGed.Application.PhysicalArchive2;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
[Route("[controller]")]
public sealed class PhysicalController : Controller
{
    private readonly ILogger<PhysicalController> _logger;
    private readonly ICurrentUser _user;
    private readonly IPhysicalQueries _queries;
    private readonly IPhysicalCommands _commands;
    private readonly IPhysicalArchive2Service _archive;

    public PhysicalController(
        ILogger<PhysicalController> logger,
        ICurrentUser user,
        IPhysicalQueries queries,
        IPhysicalCommands commands,
        IPhysicalArchive2Service archive)
    {
        _logger = logger;
        _user = user;
        _queries = queries;
        _commands = commands;
        _archive = archive;
    }

    [HttpGet("")]
    [HttpGet("Dashboard")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await _archive.DashboardAsync(_user.TenantId, ct));

    [HttpGet("Inventory")]
    public async Task<IActionResult> Inventory(CancellationToken ct)
    {
        ViewBag.Locations = await _archive.LocationsAsync(_user.TenantId, ct);
        return View(await _archive.InventoriesAsync(_user.TenantId, ct));
    }

    [HttpGet("Inventory/{id:guid}")]
    public async Task<IActionResult> InventoryDetails(Guid id, CancellationToken ct)
    {
        var model = await _archive.InventoryAsync(_user.TenantId, id, ct);
        if (model is null) return NotFound();
        ViewBag.Locations = await _archive.LocationsAsync(_user.TenantId, ct);
        return View(model);
    }

    [HttpPost("Inventory/Start"), ValidateAntiForgeryToken]
    public async Task<IActionResult> StartInventory(Guid? locationId, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title)) { TempData["Err"] = "Informe o título do inventário."; return RedirectToAction(nameof(Inventory)); }
        var id = await _archive.StartInventoryAsync(_user.TenantId, locationId, title, _user.UserId, ct);
        return RedirectToAction(nameof(InventoryDetails), new { id });
    }

    [HttpPost("Inventory/{id:guid}/Scan"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ScanInventory(Guid id, string code, Guid? foundLocationId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(code)) await _archive.ScanAsync(_user.TenantId, id, code, foundLocationId, _user.UserId, ct);
        return RedirectToAction(nameof(InventoryDetails), new { id });
    }

    [HttpPost("Inventory/{id:guid}/Close"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseInventory(Guid id, string? notes, CancellationToken ct)
    { await _archive.CloseInventoryAsync(_user.TenantId, id, _user.UserId, notes, ct); return RedirectToAction(nameof(InventoryDetails), new { id }); }

    [HttpGet("Movements")]
    public async Task<IActionResult> Movements(CancellationToken ct)
    { ViewBag.Boxes=await _archive.BoxesAsync(_user.TenantId,ct);ViewBag.Locations=await _archive.LocationsAsync(_user.TenantId,ct);return View(await _archive.MovementsAsync(_user.TenantId,ct)); }

    [HttpPost("Movements/Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMovement(Guid boxId, Guid toLocationId, string movementType, string? reason, CancellationToken ct)
    { await _archive.MoveAsync(_user.TenantId,boxId,toLocationId,movementType,reason,_user.UserId,_user.Email,ct);TempData["Ok"]="Movimentação registrada com cadeia de custódia.";return RedirectToAction(nameof(Movements)); }

    [HttpGet("Loans")]
    public async Task<IActionResult> Loans(CancellationToken ct){ViewBag.Boxes=await _archive.BoxesAsync(_user.TenantId,ct);return View(await _archive.LoansAsync(_user.TenantId,ct));}

    [HttpPost("Loans/Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLoan(Guid boxId,string requester,string? department,string? reason,DateTimeOffset? dueAt,CancellationToken ct)
    {if(string.IsNullOrWhiteSpace(requester)){TempData["Err"]="Informe o solicitante.";return RedirectToAction(nameof(Loans));}await _archive.LoanAsync(_user.TenantId,boxId,requester,department,reason,dueAt,_user.UserId,ct);return RedirectToAction(nameof(Loans));}

    [HttpPost("Loans/{id:guid}/Return"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnLoan(Guid id,string? notes,CancellationToken ct){await _archive.ReturnLoanAsync(_user.TenantId,id,notes,_user.UserId,ct);return RedirectToAction(nameof(Loans));}

    [HttpGet("Custody/{boxId:guid}")]
    public async Task<IActionResult> Custody(Guid boxId,CancellationToken ct){ViewBag.BoxId=boxId;return View(await _archive.CustodyAsync(_user.TenantId,boxId,ct));}

    [HttpGet("Labels")]
    public IActionResult Labels() => RedirectToAction(nameof(Boxes));

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
        ViewBag.History = await _queries.GetLocationHistoryAsync(_user.TenantId, id, ct);
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

    [HttpPost("Locations/{id:guid}/State")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLocationState(Guid id, bool active, string? reason, CancellationToken ct)
    {
        var res = await _commands.SetLocationActiveAsync(_user.TenantId, id, active, _user.UserId, reason, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? (active ? "Localização ativada." : "Localização inativada.") : res.ErrorMessage;
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

    [HttpPost("Boxes/{id:guid}/State")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetBoxState(Guid id, string state, bool isFull, string? reason, CancellationToken ct)
    {
        var res = await _commands.SetBoxStateAsync(_user.TenantId, id, state, isFull, _user.UserId, reason, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Situação da caixa atualizada." : res.ErrorMessage;
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
