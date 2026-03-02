using System.Text;
using InovaGed.Application.Retention;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Route("Retention/Destination")]
public sealed class RetentionDestinationController : Controller
{
    private readonly IRetentionDestinationRepository _repo;
    private readonly ILogger<RetentionDestinationController> _logger;

    // ✅ Trocar para seu contexto real
    private Guid TenantId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    private Guid UserId => Guid.Empty;

    public RetentionDestinationController(IRetentionDestinationRepository repo, ILogger<RetentionDestinationController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = await _repo.ListBatchesAsync(TenantId, ct);
        return View(list);
    }

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
            TempData["Error"] = ex.Message;
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

    [HttpPost("Execute")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Execute(Guid batchId, CancellationToken ct)
    {
        try
        {
            await _repo.ExecuteBatchAsync(TenantId, UserId, batchId, ct);
            TempData["Success"] = "Lote executado (itens sem HOLD).";
            return RedirectToAction("Details", new { batchId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute batch failed");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Details", new { batchId });
        }
    }
}