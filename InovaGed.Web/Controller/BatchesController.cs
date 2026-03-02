using InovaGed.Application.Ged.Batches;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class BatchesController : Controller
{
    private readonly ILogger<BatchesController> _logger;
    private readonly ICurrentUser _user;
    private readonly IBatchQueries _queries;
    private readonly IBatchCommands _commands;

    public BatchesController(
        ILogger<BatchesController> logger,
        ICurrentUser user,
        IBatchQueries queries,
        IBatchCommands commands)
    {
        _logger = logger;
        _user = user;
        _queries = queries;
        _commands = commands;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? status, CancellationToken ct)
    {
        try
        {
            var list = await _queries.ListAsync(_user.TenantId, q, status, ct);
            ViewBag.Q = q;
            ViewBag.Status = status;
            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.Index failed");
            return View(Array.Empty<BatchRowDto>());
        }
    }

    [HttpGet("New")]
    public IActionResult New() => View(new BatchCreateVM());

    [HttpPost("New")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(BatchCreateVM vm, CancellationToken ct)
    {
        try
        {
            var res = await _commands.CreateAsync(_user.TenantId, _user.UserId, vm, ct);
            if (!res.IsSuccess)
            {
                TempData["Err"] = res.ErrorMessage;
                return View(vm);
            }

            TempData["Ok"] = "Lote criado.";
            return RedirectToAction(nameof(Details), new { id = res.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.New failed");
            TempData["Err"] = "Erro ao criar lote.";
            return View(vm);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        try
        {
            var data = await _queries.GetAsync(_user.TenantId, id, ct);
            if (data is null) return NotFound();

            ViewBag.Header = data.Value.Header;
            ViewBag.Items = data.Value.Items;
            ViewBag.History = data.Value.History;

            return View();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.Details failed");
            return StatusCode(500);
        }
    }

    [HttpPost("{id:guid}/Status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(Guid id, string status, string? notes, CancellationToken ct)
    {
        try
        {
            var res = await _commands.ChangeStatusAsync(_user.TenantId, id, status, _user.UserId, notes, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Status atualizado." : res.ErrorMessage;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.ChangeStatus failed. Batch={Batch}", id);
            TempData["Err"] = "Erro ao alterar status.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost("{id:guid}/Items/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(Guid id, Guid documentId, Guid? boxId, CancellationToken ct)
    {
        try
        {
            var res = await _commands.AddItemAsync(_user.TenantId, id, documentId, boxId, _user.UserId, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Item adicionado." : res.ErrorMessage;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.AddItem failed");
            TempData["Err"] = "Erro ao adicionar item.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost("{id:guid}/Items/Move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveItem(Guid id, Guid documentId, Guid? newBoxId, CancellationToken ct)
    {
        try
        {
            var res = await _commands.MoveItemBoxAsync(_user.TenantId, id, documentId, newBoxId, _user.UserId, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Item movido." : res.ErrorMessage;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.MoveItem failed");
            TempData["Err"] = "Erro ao mover item.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost("{id:guid}/Items/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(Guid id, Guid documentId, CancellationToken ct)
    {
        try
        {
            var res = await _commands.RemoveItemAsync(_user.TenantId, id, documentId, _user.UserId, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Item removido." : res.ErrorMessage;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.RemoveItem failed");
            TempData["Err"] = "Erro ao remover item.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}