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

    private static readonly HashSet<string> AllowedStatus = new(StringComparer.OrdinalIgnoreCase)
    {
        "RECEIVED", "TRIAGE", "DIGITIZATION", "INDEXING", "ARCHIVED"
    };

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

    // /Batches
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? status, CancellationToken ct)
    {
        try
        {
            var normalized = NormalizeStatus(status);
            var list = await _queries.ListAsync(_user.TenantId, q, normalized, ct);

            ViewBag.Q = q;
            ViewBag.Status = normalized;
            ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();

            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.Index failed");
            TempData["Err"] = "Erro ao carregar lotes.";
            return View(Array.Empty<BatchRowDto>());
        }
    }

    // ✅ Compatibilidade: /Batches/Details (sem id) não pode ser action "coringa"
    // /Batches/Details
    [HttpGet("Details")]
    public IActionResult DetailsRedirect()
    {
        TempData["Err"] = "Selecione um lote para visualizar o detalhe.";
        return RedirectToAction(nameof(Index));
    }

    // /Batches/New
    [HttpGet("New")]
    public IActionResult New()
    {
        ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();
        return View(new BatchCreateVM());
    }

    // /Batches/New
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
                ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();
                return View(vm);
            }

            TempData["Ok"] = "Lote criado.";
            return RedirectToAction(nameof(Details), new { id = res.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.New failed");
            TempData["Err"] = "Erro ao criar lote.";
            ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();
            return View(vm);
        }
    }

    // /Batches/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        try
        {
            var data = await _queries.GetAsync(_user.TenantId, id, ct);
            if (data is null) return NotFound();

            // se sua View usa ViewBag:
            ViewBag.Header = data.Value.Header;
            ViewBag.Items = data.Value.Items;
            ViewBag.History = data.Value.History;
            ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();

            // Se sua View é fortemente tipada, troque para:
            // return View(new BatchDetailsVM { Header = ..., Items=..., History=... });

            return View();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batches.Details failed");
            TempData["Err"] = "Erro ao carregar detalhes do lote.";
            return StatusCode(500);
        }
    }

    // /Batches/{id}/Status
    [HttpPost("{id:guid}/Status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(Guid id, string status, string? notes, CancellationToken ct)
    {
        try
        {
            var normalized = NormalizeStatus(status);

            if (string.IsNullOrWhiteSpace(normalized) || !AllowedStatus.Contains(normalized))
            {
                TempData["Err"] = "Status inválido. Use: RECEIVED, TRIAGE, DIGITIZATION, INDEXING, ARCHIVED.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var res = await _commands.ChangeStatusAsync(_user.TenantId, id, normalized, _user.UserId, notes, ct);
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

    // /Batches/{id}/Items/Add
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

    // /Batches/{id}/Items/Move
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

    // /Batches/{id}/Items/Remove
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

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        var s = status.Trim();

        if (AllowedStatus.Contains(s)) return s.ToUpperInvariant();

        return s.ToUpperInvariant() switch
        {
            "RECEBIDO" or "RECEIVED" => "RECEIVED",
            "TRIAGEM" or "TRIAGE" => "TRIAGE",
            "DIGITALIZACAO" or "DIGITALIZAÇÃO" or "DIGITIZACAO" or "DIGITIZATION" => "DIGITIZATION",
            "INDEXACAO" or "INDEXAÇÃO" or "INDEXING" => "INDEXING",
            "ARQUIVADO" or "ARQUIVAMENTO" or "ARCHIVED" => "ARCHIVED",
            _ => s.ToUpperInvariant()
        };
    }
}