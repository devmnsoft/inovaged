using InovaGed.Application.Common.Context;
using InovaGed.Application.Parameters;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.ParametersAdmin)]
[Route("Parameters")]
public sealed class ParametersController : Controller
{
    private readonly IParameterRepository _repo;
    private readonly ICurrentContext _ctx;
    private readonly ILogger<ParametersController> _logger;

    private Guid TenantId => _ctx.TenantId;
    private Guid UserId => _ctx.UserId;

    public ParametersController(IParameterRepository repo, ICurrentContext ctx, ILogger<ParametersController> logger)
    {
        _repo = repo;
        _ctx = ctx;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? categoryCode, string? search, string status = "all", CancellationToken ct = default)
    {
        var categories = await _repo.ListCategoriesAsync(TenantId, ct);
        categoryCode = string.IsNullOrWhiteSpace(categoryCode) ? null : categoryCode.Trim().ToUpperInvariant();

        var items = await _repo.ListItemsAsync(TenantId, categoryCode, search, ct);
        status = status is "active" or "inactive" ? status : "all";
        if (status != "all") items = items.Where(x => x.IsActive == (status == "active")).ToList();

        return View(new ParameterIndexVM
        {
            Categories = categories,
            Items = items,
            CategoryCode = categoryCode,
            Search = search,
            Status = status
        });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(string? categoryCode, CancellationToken ct)
    {
        var categories = await _repo.ListCategoriesAsync(TenantId, ct);
        var category = categories.FirstOrDefault(x => string.Equals(x.Code, categoryCode, StringComparison.OrdinalIgnoreCase))
            ?? categories.FirstOrDefault();

        if (category is null)
        {
            TempData["Error"] = "Nenhuma categoria de parâmetro cadastrada.";
            return RedirectToAction(nameof(Index));
        }

        var vm = new ParameterItemEditVM
        {
            CategoryId = category.Id,
            IsActive = true,
            DisplayOrder = 0
        };

        await LoadCombos(vm, ct);
        return View("Edit", vm);
    }

    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var vm = await _repo.GetItemAsync(TenantId, id, ct);
        if (vm is null) return NotFound();

        await LoadCombos(vm, ct);
        return View(vm);
    }

    [HttpGet("Details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var item = (await _repo.ListItemsAsync(TenantId, null, null, ct)).FirstOrDefault(x => x.Id == id);
        return item is null ? NotFound(new { message = "Parâmetro não encontrado." }) : Json(item);
    }

    [HttpGet("Duplicate/{id:guid}")]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct)
    {
        var vm = await _repo.GetItemAsync(TenantId, id, ct);
        if (vm is null) return NotFound();
        vm.Id = null;
        vm.Code = null;
        vm.Name = $"{vm.Name} (cópia)";
        vm.IsDefault = false;
        await LoadCombos(vm, ct);
        return View("Edit", vm);
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ParameterItemEditVM vm, CancellationToken ct)
    {
        var isCreate = !vm.Id.HasValue || vm.Id.Value == Guid.Empty;
        if (isCreate)
        {
            ModelState.Remove(nameof(vm.Id));
            ModelState.Remove(nameof(vm.Code));
            ModelState.Remove("Id");
            ModelState.Remove("Code");
            vm.Id = null;
            vm.Code = null;
        }

        if (string.IsNullOrWhiteSpace(vm.Code))
        {
            ModelState.Remove(nameof(vm.Code));
            ModelState.Remove("Code");
        }

        if (vm.CategoryId == Guid.Empty)
            ModelState.AddModelError(nameof(vm.CategoryId), "Categoria obrigatória.");

        if (string.IsNullOrWhiteSpace(vm.Name))
            ModelState.AddModelError(nameof(vm.Name), "Nome obrigatório.");

        if (!string.IsNullOrWhiteSpace(vm.MetadataJson))
        {
            try { _ = JsonDocument.Parse(vm.MetadataJson); }
            catch (JsonException) { ModelState.AddModelError(nameof(vm.MetadataJson), "Metadados JSON inválidos. Revise a sintaxe antes de salvar."); }
        }

        if (!ModelState.IsValid)
        {
            await LoadCombos(vm, ct);
            return View("Edit", vm);
        }

        try
        {
            await _repo.UpsertItemAsync(TenantId, UserId, vm, ct);
            TempData["Success"] = "Parâmetro salvo com sucesso.";
            var categories = await _repo.ListCategoriesAsync(TenantId, ct);
            var categoryCode = categories.FirstOrDefault(x => x.Id == vm.CategoryId)?.Code;
            return RedirectToAction(nameof(Index), new { categoryCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao salvar parâmetro");
            var message = ex.Message.Contains("ged.code_sequence", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("gerar o código", StringComparison.OrdinalIgnoreCase)
                ? "Não foi possível gerar o código automático. Execute as migrations do sistema."
                : ex is ArgumentException or InvalidOperationException ? ex.Message : $"Não foi possível salvar o parâmetro. Tente novamente. Referência: {HttpContext.TraceIdentifier}";
            ModelState.AddModelError(string.Empty, message);
            await LoadCombos(vm, ct);
            return View("Edit", vm);
        }
    }

    [HttpPost("SetActive/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid id, bool active, string? categoryCode, CancellationToken ct)
    {
        try
        {
            await _repo.SetActiveAsync(TenantId, UserId, id, active, ct);
            TempData["Success"] = active ? "Parâmetro ativado." : "Parâmetro inativado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao alterar status do parâmetro {ParameterId}", id);
            TempData["Error"] = $"Não foi possível alterar o status. Referência: {HttpContext.TraceIdentifier}";
        }
        return RedirectToAction(nameof(Index), new { categoryCode });
    }

    [HttpPost("Delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? categoryCode, string? reason, CancellationToken ct)
    {
        try
        {
            await _repo.DeleteAsync(TenantId, UserId, id, reason, ct);
            TempData["Success"] = "Parâmetro excluído logicamente.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao inativar parâmetro {ParameterId}", id);
            TempData["Error"] = ex is InvalidOperationException ? ex.Message : $"Não foi possível inativar o parâmetro. Referência: {HttpContext.TraceIdentifier}";
        }

        return RedirectToAction(nameof(Index), new { categoryCode });
    }

    private async Task LoadCombos(ParameterItemEditVM vm, CancellationToken ct)
    {
        var categories = await _repo.ListCategoriesAsync(TenantId, ct);
        ViewBag.Categories = categories.Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == vm.CategoryId)).ToList();

        var selectedCategory = categories.FirstOrDefault(x => x.Id == vm.CategoryId);
        ViewBag.SelectedCategory = selectedCategory;

        if (selectedCategory?.AllowHierarchy == true)
        {
            var parents = await _repo.ListParentOptionsAsync(TenantId, vm.CategoryId, vm.Id, ct);
            ViewBag.Parents = parents.Select(p => new SelectListItem($"{p.Code} - {p.Name}", p.Id.ToString(), p.Id == vm.ParentId)).ToList();
        }
        else
        {
            ViewBag.Parents = new List<SelectListItem>();
        }
    }
}
