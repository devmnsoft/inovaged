using System.Text;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Retention;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Route("[controller]")]
public sealed class RetentionController : Controller
{
    private readonly IRetentionJobRepository _repo;
    private readonly RetentionRecalcService _svc;
    private readonly IRetentionQueueQueries _queue;
    private readonly IRetentionAuditWriter _audit;
    private readonly ILogger<RetentionController> _logger;
    private readonly ICurrentContext _ctx;

   


    // ✅ Ajuste para seu contexto real depois
    private Guid TenantId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    private Guid UserId => Guid.Empty;

    public RetentionController(
        IRetentionJobRepository repo,
        RetentionRecalcService svc,
        IRetentionQueueQueries queue,
        IRetentionAuditWriter audit,
        ILogger<RetentionController> logger,
        ICurrentContext ctx)
    {
        _repo = repo;
        _svc = svc;
        _queue = queue;
        _audit = audit;
        _logger = logger;
        _ctx = ctx;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = await _repo.GetDashboardAsync(TenantId, dueSoonDays: 30, ct);
        return View(vm);
    }

    [HttpPost("Recalculate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recalculate(CancellationToken ct)
    {
        await _svc.RunAsync(TenantId, dueSoonDays: 30, ct);
        TempData["Success"] = "Temporalidade recalculada com sucesso.";
        return RedirectToAction("Index");
    }

    // ✅ NOVO: Central Operacional
    [HttpGet("Queue")]
    public async Task<IActionResult> Queue(string? status, DateTimeOffset? dueUntil, string? q, CancellationToken ct)
    {
        try
        {
            var filter = new RetentionQueueFilter { Status = status, DueUntil = dueUntil, Q = q };
            var rows = await _queue.ListAsync(TenantId, filter, ct);

            ViewBag.Status = status;
            ViewBag.DueUntil = dueUntil;
            ViewBag.Q = q;

            return View(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention Queue error");
            TempData["Error"] = "Erro ao carregar a central de temporalidade.";
            return View(Array.Empty<RetentionQueueRow>());
        }
    }

    public sealed class ExportRequest
    {
        public Guid[] DocumentIds { get; set; } = Array.Empty<Guid>();
    }

    // ✅ NOVO: Export CSV dos selecionados
    [HttpPost("ExportCsv")]
    public async Task<IActionResult> ExportCsv([FromBody] ExportRequest req, CancellationToken ct)
    {
        if (req.DocumentIds.Length == 0)
            return BadRequest(new { ok = false, error = "Selecione ao menos 1 documento." });

        var rows = await _queue.ListByIdsAsync(TenantId, req.DocumentIds, ct);

        // Auditoria (1 por doc)
        foreach (var r in rows)
            await _audit.WriteAsync(TenantId, UserId, r.DocumentId, "EXPORT_CSV", null, ct);

        var csv = BuildCsv(rows);
        var bytes = Encoding.UTF8.GetBytes(csv);

        var fileName = $"temporalidade_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static string BuildCsv(IReadOnlyList<RetentionQueueRow> rows)
    {
        static string Esc(string? s)
        {
            s ??= "";
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        var sb = new StringBuilder();
        sb.AppendLine("document_id,doc_code,doc_title,class_code,class_name,due_at,status");

        foreach (var r in rows)
        {
            sb.Append(Esc(r.DocumentId.ToString())).Append(',')
              .Append(Esc(r.Code)).Append(',')
              .Append(Esc(r.Title)).Append(',')
              .Append(Esc(r.ClassificationCode)).Append(',')
              .Append(Esc(r.ClassificationName)).Append(',')
              .Append(Esc(r.DueAt?.ToString("yyyy-MM-dd HH:mm"))).Append(',')
              .Append(Esc(r.Status))
              .AppendLine();
        }

        return sb.ToString();
    }
}