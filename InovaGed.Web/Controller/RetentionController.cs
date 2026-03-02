using System.Text;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Retention;
using InovaGed.Infrastructure.Retention;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Route("[controller]")]
public sealed class RetentionController : Controller
{
    private readonly IRetentionJobRepository _repo;
    private readonly RetentionRecalculateService _svc;
    private readonly IRetentionQueueQueries _queue;
    private readonly IRetentionAuditWriter _audit;
    private readonly ILogger<RetentionController> _logger;
    private readonly ICurrentContext _ctx;

    public RetentionController(
        IRetentionJobRepository repo,
        RetentionRecalculateService svc,
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

    private Guid TenantIdOrThrow()
    {
        var tid = _ctx.TenantId;
        if (tid == Guid.Empty) throw new InvalidOperationException("TenantId não encontrado no contexto.");
        return tid;
    }

    private Guid UserIdOrEmpty() => _ctx.UserId;

    // GET /Retention
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();

        // seu dashboard atual
        var vm = await _repo.GetDashboardAsync(tenantId, dueSoonDays: 30, ct);
        return View(vm);
    }

    // POST /Retention/Recalculate
    [HttpPost("Recalculate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recalculate(CancellationToken ct)
    {
        try
        {
            var tenantId = TenantIdOrThrow();

            // ✅ recalcular (manual)
            // Se seu service retorna dados, você pode mostrar em TempData (igual no TemporalidadeController)
            await _svc.ExecuteAsync(tenantId, UserIdOrEmpty(), ct);

            TempData["Success"] = "Temporalidade recalculada com sucesso.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention.Recalculate failed Tenant={Tenant}", _ctx.TenantId);
            TempData["Error"] = "Falha ao recalcular temporalidade. Ver logs.";
        }

        return RedirectToAction(nameof(Index));
    }

    // ✅ Central Operacional
    // GET /Retention/Queue?status=OVERDUE&dueUntil=...&q=...
    [HttpGet("Queue")]
    public async Task<IActionResult> Queue(string? status, DateTimeOffset? dueUntil, string? q, CancellationToken ct)
    {
        try
        {
            var tenantId = TenantIdOrThrow();

            var filter = new RetentionQueueFilter
            {
                Status = status,
                DueUntil = dueUntil,
                Q = q
            };

            var rows = await _queue.ListAsync(tenantId, filter, ct);

            ViewBag.Status = status;
            ViewBag.DueUntil = dueUntil;
            ViewBag.Q = q;

            return View(rows); // Views/Retention/Queue.cshtml
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention.Queue error Tenant={Tenant}", _ctx.TenantId);
            TempData["Error"] = "Erro ao carregar a central operacional de temporalidade.";
            return View(Array.Empty<RetentionQueueRow>());
        }
    }

    public sealed class ExportRequest
    {
        public Guid[] DocumentIds { get; set; } = Array.Empty<Guid>();
    }

    // ✅ Export CSV dos selecionados
    // POST /Retention/ExportCsv  (JSON body)
    [HttpPost("ExportCsv")]
    // Se quiser antiforgery depois, você vai precisar mandar o token no header do fetch.
    // [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportCsv([FromBody] ExportRequest req, CancellationToken ct)
    {
        try
        {
            var tenantId = TenantIdOrThrow();

            if (req?.DocumentIds == null || req.DocumentIds.Length == 0)
                return BadRequest(new { ok = false, error = "Selecione ao menos 1 documento." });

            var rows = await _queue.ListByIdsAsync(tenantId, req.DocumentIds, ct);

            // Auditoria (1 por doc)
            foreach (var r in rows)
                await _audit.WriteAsync(tenantId, UserIdOrEmpty(), r.DocumentId, "EXPORT_CSV", null, ct);

            var csv = BuildCsv(rows);
            var bytes = Encoding.UTF8.GetBytes(csv);

            var fileName = $"temporalidade_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention.ExportCsv failed Tenant={Tenant}", _ctx.TenantId);
            return BadRequest(new { ok = false, error = "Falha ao exportar CSV. Ver logs." });
        }
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

        // ✅ incluo destino sugerido agora (já existe na sua Row)
        sb.AppendLine("document_id,doc_code,doc_title,class_code,class_name,due_at,status,suggested_destination");

        foreach (var r in rows)
        {
            sb.Append(Esc(r.DocumentId.ToString())).Append(',')
              .Append(Esc(r.Code)).Append(',')
              .Append(Esc(r.Title)).Append(',')
              .Append(Esc(r.ClassificationCode)).Append(',')
              .Append(Esc(r.ClassificationName)).Append(',')
              .Append(Esc(r.DueAt?.ToString("yyyy-MM-dd HH:mm"))).Append(',')
              .Append(Esc(r.Status)).Append(',')
              .Append(Esc(r.SuggestedDestination))
              .AppendLine();
        }

        return sb.ToString();
    }
}