using InovaGed.Application.Common.Context;
using InovaGed.Application.Parameters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Parameters")]
public sealed class ParametersController : Controller
{
    private readonly IParameterRepository _repo;
    private readonly ICurrentContext _ctx;
    private readonly ILogger<ParametersController> _logger;

    private Guid TenantId => _ctx.TenantId == Guid.Empty
        ? Guid.Parse("00000000-0000-0000-0000-000000000001")
        : _ctx.TenantId;

    private Guid UserId => _ctx.UserId == Guid.Empty ? Guid.Empty : _ctx.UserId;

    public ParametersController(IParameterRepository repo, ICurrentContext ctx, ILogger<ParametersController> logger)
    {
        _repo = repo;
        _ctx = ctx;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? categoryCode, string? search, CancellationToken ct)
    {
        var categories = await _repo.ListCategoriesAsync(TenantId, ct);
        categoryCode = string.IsNullOrWhiteSpace(categoryCode)
            ? categories.FirstOrDefault()?.Code
            : categoryCode.Trim().ToUpperInvariant();

        var items = await _repo.ListItemsAsync(TenantId, categoryCode, search, ct);

        return View(new ParameterIndexVM
        {
            Categories = categories,
            Items = items,
            CategoryCode = categoryCode,
            Search = search
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

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ParameterItemEditVM vm, CancellationToken ct)
    {
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
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadCombos(vm, ct);
            return View("Edit", vm);
        }
    }

    [HttpPost("SetActive/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid id, bool active, string? categoryCode, CancellationToken ct)
    {
        await _repo.SetActiveAsync(TenantId, UserId, id, active, ct);
        TempData["Success"] = active ? "Parâmetro ativado." : "Parâmetro inativado.";
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
            TempData["Error"] = ex.Message;
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
