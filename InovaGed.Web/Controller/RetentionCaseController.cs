using System.Text;
using InovaGed.Application.RetentionCases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Route("RetentionCases")]
public sealed class RetentionCaseController : Controller
{
    private readonly IRetentionCaseRepository _repo;
    private readonly ILogger<RetentionCaseController> _logger;

    // ✅ Ajuste para seu contexto real depois
    private Guid TenantId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    private Guid UserId => Guid.Empty;

    public RetentionCaseController(IRetentionCaseRepository repo, ILogger<RetentionCaseController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        var list = await _repo.ListAsync(TenantId, status, ct);
        ViewBag.Status = status;
        return View(list);
    }

    [HttpGet("Details")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var data = await _repo.GetAsync(TenantId, id, ct);
        if (data is null) return NotFound();
        return View(data.Value);
    }

    [HttpPost("Create")]
    public async Task<IActionResult> Create([FromBody] CreateRetentionCaseRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _repo.CreateAsync(TenantId, UserId, req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create case failed");
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost("DecideItem")]
    public async Task<IActionResult> DecideItem([FromBody] DecideItemRequest req, CancellationToken ct)
    {
        try
        {
            await _repo.DecideItemAsync(TenantId, UserId, req, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decide item failed");
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost("Close")]
    public async Task<IActionResult> Close(Guid caseId, string status, CancellationToken ct)
    {
        try
        {
            await _repo.CloseCaseAsync(TenantId, UserId, caseId, status, ct);
            TempData["Success"] = $"Caso encerrado como {status}.";
            return RedirectToAction("Details", new { id = caseId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Close case failed");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Details", new { id = caseId });
        }
    }

    [HttpGet("Print")]
    public async Task<IActionResult> Print(Guid caseId, CancellationToken ct)
    {
        var data = await _repo.GetAsync(TenantId, caseId, ct);
        if (data is null) return NotFound();
        return View("Print", data.Value);
    }

    [HttpPost("Execute")]
    public async Task<IActionResult> Execute(Guid caseId, CancellationToken ct,
    [FromServices] RetentionCaseExecutionService execSvc)
    {
        try
        {
            await execSvc.ExecuteAsync(TenantId, UserId, caseId, ct);
            TempData["Success"] = "Caso executado com sucesso.";
            return RedirectToAction("Details", new { id = caseId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute case failed");
            TempData["Error"] = ex.Message;
            return RedirectToAction("Details", new { id = caseId });
        }
    }

    [HttpGet("ExportCaseCsv")]
    public async Task<IActionResult> ExportCaseCsv(Guid caseId, CancellationToken ct)
    {
        var data = await _repo.GetAsync(TenantId, caseId, ct);
        if (data is null) return NotFound();

        var (c, items) = data.Value;

        var sb = new StringBuilder();
        sb.AppendLine("item_id,document_id,doc_code,doc_title,class_code,class_name,due_at,status,suggested_destination,decision,executed_at");
        foreach (var i in items)
        {
            string Esc(string? s)
            {
                s ??= "";
                if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                    return "\"" + s.Replace("\"", "\"\"") + "\"";
                return s;
            }

            sb.Append(Esc(i.Id.ToString())).Append(',')
              .Append(Esc(i.DocumentId.ToString())).Append(',')
              .Append(Esc(i.DocCode)).Append(',')
              .Append(Esc(i.DocTitle)).Append(',')
              .Append(Esc(i.ClassificationCode)).Append(',')
              .Append(Esc(i.ClassificationName)).Append(',')
              .Append(Esc(i.RetentionDueAt?.ToString("yyyy-MM-dd"))).Append(',')
              .Append(Esc(i.RetentionStatus)).Append(',')
              .Append(Esc(i.SuggestedDestination)).Append(',')
              .Append(Esc(i.Decision)).Append(',')
              .Append(Esc(i.DecidedAt?.ToString("yyyy-MM-dd HH:mm")))
              .AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"caso_{c.CaseNo:0000}_{DateTime.Now:yyyyMMdd}.csv");
    }
}