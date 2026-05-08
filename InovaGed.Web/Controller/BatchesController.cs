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
            _logger.LogError(ex, "Erro ao carregar lotes.");
            TempData["Err"] = "Não foi possível carregar os lotes. Tente novamente.";
            return View(Array.Empty<BatchRowDto>());
        }
    }

    [HttpGet("Details")]
    public IActionResult DetailsRedirect()
    {
        TempData["Err"] = "Selecione um lote para visualizar os detalhes.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("New")]
    public IActionResult New()
    {
        ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();
        return View(new BatchCreateVM());
    }

    [HttpPost("New")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(BatchCreateVM vm, CancellationToken ct)
    {
        try
        {
            vm.DocumentIds = vm.DocumentIds?
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList() ?? new List<Guid>();

            if (vm.BoxId.HasValue && vm.BoxId.Value == Guid.Empty)
                vm.BoxId = null;

            if (string.IsNullOrWhiteSpace(vm.Notes) && vm.DocumentIds.Count == 0)
            {
                TempData["Err"] = "Informe uma observação ou selecione pelo menos um documento para criar o lote.";
                ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();
                return View(vm);
            }

            var result = await _commands.CreateAsync(_user.TenantId, _user.UserId, vm, ct);

            if (!result.IsSuccess)
            {
                TempData["Err"] = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Não foi possível criar o lote."
                    : result.ErrorMessage;

                ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();
                return View(vm);
            }

            TempData["Ok"] = vm.DocumentIds.Count > 0
                ? $"Lote criado com sucesso com {vm.DocumentIds.Count} documento(s)."
                : "Lote criado com sucesso.";

            return RedirectToAction(nameof(Details), new { id = result.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar lote.");
            TempData["Err"] = "Erro inesperado ao criar o lote. A ocorrência foi registrada no log.";
            ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();
            return View(vm);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        try
        {
            var data = await _queries.GetAsync(_user.TenantId, id, ct);

            if (data is null)
            {
                TempData["Err"] = "Lote não encontrado ou sem permissão de acesso.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Header = data.Value.Header;
            ViewBag.Items = data.Value.Items;
            ViewBag.History = data.Value.History;
            ViewBag.AllowedStatus = AllowedStatus.OrderBy(x => x).ToArray();

            return View();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar detalhe do lote {BatchId}", id);
            TempData["Err"] = "Erro ao carregar os detalhes do lote.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("{id:guid}/Status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(Guid id, string status, string? notes, CancellationToken ct)
    {
        try
        {
            var normalized = NormalizeStatus(status);

            if (string.IsNullOrWhiteSpace(normalized) || !AllowedStatus.Contains(normalized))
            {
                TempData["Err"] = "Etapa inválida. Selecione uma etapa válida do tratamento documental.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var result = await _commands.ChangeStatusAsync(_user.TenantId, id, normalized, _user.UserId, notes, ct);

            TempData[result.IsSuccess ? "Ok" : "Err"] = result.IsSuccess
                ? $"Etapa do lote atualizada para {StatusLabel(normalized)} com sucesso."
                : result.ErrorMessage ?? "Não foi possível atualizar a etapa do lote.";

            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao alterar status do lote {BatchId}", id);
            TempData["Err"] = "Erro ao alterar a etapa do lote. A ocorrência foi registrada.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost("{id:guid}/Items/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(Guid id, Guid documentId, Guid? boxId, CancellationToken ct)
    {
        try
        {
            if (documentId == Guid.Empty)
            {
                TempData["Err"] = "Documento inválido.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (boxId.HasValue && boxId.Value == Guid.Empty)
                boxId = null;

            var result = await _commands.AddItemAsync(_user.TenantId, id, documentId, boxId, _user.UserId, ct);

            TempData[result.IsSuccess ? "Ok" : "Err"] = result.IsSuccess
                ? "Documento adicionado ao lote com sucesso."
                : result.ErrorMessage ?? "Não foi possível adicionar o documento ao lote.";

            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao adicionar item ao lote {BatchId}", id);
            TempData["Err"] = "Erro ao adicionar documento ao lote.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost("{id:guid}/Items/Move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveItem(Guid id, Guid documentId, Guid? newBoxId, CancellationToken ct)
    {
        try
        {
            if (documentId == Guid.Empty)
            {
                TempData["Err"] = "Documento inválido.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (newBoxId.HasValue && newBoxId.Value == Guid.Empty)
                newBoxId = null;

            var result = await _commands.MoveItemBoxAsync(_user.TenantId, id, documentId, newBoxId, _user.UserId, ct);

            TempData[result.IsSuccess ? "Ok" : "Err"] = result.IsSuccess
                ? "Documento movimentado entre caixas com sucesso."
                : result.ErrorMessage ?? "Não foi possível movimentar o documento.";

            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao mover item do lote {BatchId}", id);
            TempData["Err"] = "Erro ao movimentar documento.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost("{id:guid}/Items/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(Guid id, Guid documentId, CancellationToken ct)
    {
        try
        {
            if (documentId == Guid.Empty)
            {
                TempData["Err"] = "Documento inválido.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var result = await _commands.RemoveItemAsync(_user.TenantId, id, documentId, _user.UserId, ct);

            TempData[result.IsSuccess ? "Ok" : "Err"] = result.IsSuccess
                ? "Documento removido do lote com sucesso."
                : result.ErrorMessage ?? "Não foi possível remover o documento do lote.";

            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao remover item do lote {BatchId}", id);
            TempData["Err"] = "Erro ao remover documento do lote.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpGet("DocumentsSearch")]
    public async Task<IActionResult> DocumentsSearch(string? q, int take = 20, string? status = null, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
                return Json(Array.Empty<object>());

            take = Math.Clamp(take, 5, 50);

            var rows = await _queries.SearchDocumentsAsync(_user.TenantId, q.Trim(), take, status, ct);

            return Json(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na busca de documentos para lote. Q={Q}", q);
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Json(new { error = "Erro ao buscar documentos." });
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

    private static string StatusLabel(string status)
    {
        return status switch
        {
            "RECEIVED" => "Recebido",
            "TRIAGE" => "Triagem",
            "DIGITIZATION" => "Digitalização",
            "INDEXING" => "Indexação",
            "ARCHIVED" => "Arquivado",
            _ => status
        };
    }
}