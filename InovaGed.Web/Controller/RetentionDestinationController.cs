using System.Text;
using InovaGed.Application.Identity;
using InovaGed.Application.Retention;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Web.Security;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Route("Retention/Destination")]
[Authorize]
public sealed class RetentionDestinationController : Controller
{
    private readonly IRetentionDestinationRepository _repo;
    private readonly ILogger<RetentionDestinationController> _logger;
    private readonly ICurrentUser _currentUser;

    private Guid TenantId => _currentUser.TenantId;
    private Guid UserId => _currentUser.UserId;

    public RetentionDestinationController(IRetentionDestinationRepository repo, ILogger<RetentionDestinationController> logger, ICurrentUser currentUser)
    {
        _repo = repo;
        _logger = logger;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = await _repo.ListBatchesAsync(TenantId, ct);
        return View(list);
    }

    [Authorize(Policy = AppPolicies.RetentionManage)]
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DestinationCreateRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _repo.CreateBatchAsync(TenantId, UserId, req, ct);
            TempData["Success"] = "Lote criado.";
            return RedirectToAction("Details", new { batchId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create batch failed");
            TempData["Error"] = "Não foi possível criar o lote de destinação. Tente novamente ou contate o suporte.";
            return RedirectToAction("Index");
        }
    }

    [HttpGet("Details")]
    public async Task<IActionResult> Details(Guid batchId, CancellationToken ct)
    {
        var items = await _repo.GetBatchItemsAsync(TenantId, batchId, ct);
        ViewBag.BatchId = batchId;
        return View(items);
    }

    [HttpGet("ExportCsv")]
    public async Task<IActionResult> ExportCsv(Guid batchId, CancellationToken ct)
    {
        try
        {
            var csv = await _repo.ExportBatchCsvAsync(TenantId, UserId, batchId, ct);
            var bytes = Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv; charset=utf-8", $"lote_destinacao_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export batch csv failed");
            TempData["Error"] = "Falha ao exportar CSV.";
            return RedirectToAction("Details", new { batchId });
        }
    }

    [Authorize(Policy = AppPolicies.RetentionManage)]
    [HttpPost("Execute")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Execute(Guid batchId, CancellationToken ct)
    {
        try
        {
            var r = await _repo.ExecuteBatchAsync(TenantId, UserId, batchId, ct);
            if (r.AlreadyExecuted)
            {
                TempData["Success"] = "Lote já estava executado.";
            }
            else if (r.Executed > 0 && r.Blocked > 0)
            {
                TempData["Success"] = $"Execução parcial: {r.Executed} executado(s), {r.Blocked} bloqueado(s) por empréstimo/movimentação/protocolo/hold ativo. Motivos visíveis na coluna Bloqueio.";
            }
            else if (r.Blocked > 0)
            {
                TempData["Error"] = $"Nenhum item executado: {r.Blocked} bloqueado(s) (empréstimo físico, movimentação, protocolo pendente ou hold legal). Libere os bloqueios e execute novamente.";
            }
            else
            {
                TempData["Success"] = $"Lote executado: {r.Executed} documento(s).";
            }
            return RedirectToAction("Details", new { batchId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute batch failed");
            TempData["Error"] = "Não foi possível executar o lote de destinação. Tente novamente ou contate o suporte.";
            return RedirectToAction("Details", new { batchId });
        }
    }
}
