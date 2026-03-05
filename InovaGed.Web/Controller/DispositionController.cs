using System.Text;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Disposition")]
public sealed class DispositionController : Controller
{
    private readonly IDispositionReportsQueries _q;
    private readonly ICurrentContext _ctx;
    private readonly ILogger<DispositionController> _logger;

    private Guid TenantId => _ctx.TenantId;

    public DispositionController(
        IDispositionReportsQueries q,
        ICurrentContext ctx,
        ILogger<DispositionController> logger)
    {
        _q = q;
        _ctx = ctx;
        _logger = logger;
    }

    // GET /Disposition
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var kpis = await _q.GetKpisAsync(TenantId, ct);
        return View(kpis);
    }

    // GET /Disposition/Queue
    [HttpGet("Queue")]
    public async Task<IActionResult> Queue(
        string? status,
        string? q,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var filter = new DispositionFilter
        {
            Status = status,
            Q = q,
            From = from,
            To = to
        };

        var result = await _q.ListDispositionPagedAsync(TenantId, filter, page, pageSize, ct);

        ViewBag.Status = status;
        ViewBag.Q = q;
        ViewBag.From = from;
        ViewBag.To = to;

        return View(result);
    }

    // GET /Disposition/QueueCsv
    [HttpGet("QueueCsv")]
    public async Task<IActionResult> QueueCsv(
        string? status,
        string? q,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var filter = new DispositionFilter
        {
            Status = status,
            Q = q,
            From = from,
            To = to
        };

        var rows = await _q.ListDispositionAsync(TenantId, filter, ct);

        var sb = new StringBuilder();
        sb.AppendLine("document_id,doc_code,doc_title,disposition_status,disposition_at,case_id,class_code,class_name,retention_due_at,retention_status");

        static string Esc(string? s)
        {
            s ??= "";
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        foreach (var r in rows)
        {
            sb.Append(Esc(r.DocumentId.ToString())).Append(',')
              .Append(Esc(r.DocCode)).Append(',')
              .Append(Esc(r.DocTitle)).Append(',')
              .Append(Esc(r.DispositionStatus)).Append(',')
              .Append(Esc(r.DispositionAt?.ToString("yyyy-MM-dd HH:mm"))).Append(',')
              .Append(Esc(r.CaseId?.ToString())).Append(',')
              .Append(Esc(r.ClassCode)).Append(',')
              .Append(Esc(r.ClassName)).Append(',')
              .Append(Esc(r.RetentionDueAt?.ToString("yyyy-MM-dd"))).Append(',')
              .Append(Esc(r.RetentionStatus))
              .AppendLine();
        }

        return File(
            Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv; charset=utf-8",
            $"disposition_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    // GET /Disposition/Terms
    [HttpGet("Terms")]
    public async Task<IActionResult> Terms(DateTimeOffset? from, DateTimeOffset? to, string? status, CancellationToken ct)
    {
        // Normaliza por dia (UTC 00:00 / < próximo dia)
        DateTimeOffset? fromUtc = null;
        if (from is not null)
            fromUtc = new DateTimeOffset(from.Value.Date, TimeSpan.Zero);

        DateTimeOffset? toUtc = null;
        if (to is not null)
            toUtc = new DateTimeOffset(to.Value.Date.AddDays(1), TimeSpan.Zero);

        var list = await _q.ListTermsAsync(TenantId, fromUtc, toUtc, status, ct);

        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Status = status;

        return View(list);
    }
}