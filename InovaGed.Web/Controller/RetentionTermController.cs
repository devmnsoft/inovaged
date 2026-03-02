using InovaGed.Application.Common.Context;
using InovaGed.Application.Reports;
using InovaGed.Application.RetentionTerms;
using InovaGed.Infrastructure.RetentionTerms;
using InovaGed.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize(Policy = Policies.CanViewRetention)]
[Route("RetentionTerms")]
public sealed class RetentionTermController : Controller
{
    private readonly IRetentionTermRepository _repo;
    private readonly ILogger<RetentionTermController> _logger;
    private readonly ICurrentContext _ctx;
    private readonly IDispositionReportsQueries _reports;

    public RetentionTermController(
        IRetentionTermRepository repo,
        ILogger<RetentionTermController> logger,
        ICurrentContext ctx,
        IDispositionReportsQueries reports)
    {
        _repo = repo;
        _logger = logger;
        _ctx = ctx;
        _reports = reports;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateTimeOffset? from, DateTimeOffset? to, string? status, CancellationToken ct)
    {
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Status = status;

        var rows = await _repo.ListAsync(_ctx.TenantId, from, to, status, ct);
        return View(rows); // Views/RetentionTerm/Index.cshtml
    }

    // GET /RetentionTerms/CreateFromCase?caseId=...
    [HttpGet("CreateFromCase")]
    public async Task<IActionResult> CreateFromCase(Guid caseId, CancellationToken ct)
    {
        try
        {
            var req = new CreateTermRequest
            {
                CaseId = caseId,
                TermType = "ELIMINATION",
                Notes = $"Gerado via fluxo Temporalidade em {DateTimeOffset.Now:dd/MM/yyyy HH:mm}."
            };

            var termId = await _repo.CreateFromCaseAsync(_ctx.TenantId, _ctx.UserId, req, ct);

            TempData["Success"] = "Termo criado com sucesso.";
            return RedirectToAction(nameof(Details), new { id = termId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateFromCase failed Tenant={Tenant} Case={Case}", _ctx.TenantId, caseId);
            TempData["Error"] = "Falha ao criar termo a partir do caso.";
            return Redirect("/Temporalidade");
        }
    }

    // ✅ FALTAVA ESTE ENDPOINT (sua View chama ele)
    [HttpPost("ReadyToSign")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanSignRetention)]
    public async Task<IActionResult> ReadyToSign(Guid termId, CancellationToken ct)
    {
        try
        {
            await _repo.MarkReadyToSignAsync(_ctx.TenantId, _ctx.UserId, termId, ct);
            TempData["Success"] = "Termo marcado como READY_TO_SIGN.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadyToSign failed Tenant={Tenant} Term={Term}", _ctx.TenantId, termId);
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = termId });
    }

    [HttpPost("Sign")]
    [Authorize(Policy = Policies.CanSignRetention)]
    public async Task<IActionResult> Sign([FromBody] SignTermRequest req, CancellationToken ct)
    {
        try
        {
            await _repo.SignAsync(_ctx.TenantId, _ctx.UserId, req, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sign failed Tenant={Tenant} Term={Term}", _ctx.TenantId, req.TermId);
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost("ExecuteFinal")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanExecuteFinal)]
    public async Task<IActionResult> ExecuteFinal(Guid termId, CancellationToken ct)
    {
        try
        {
            await _repo.ExecuteFinalAsync(_ctx.TenantId, _ctx.UserId, termId, ct);
            TempData["Success"] = "Execução final concluída.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteFinal failed Tenant={Tenant} Term={Term}", _ctx.TenantId, termId);
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = termId });
    }

    [HttpGet("Pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct, [FromServices] ITermPdfGenerator pdf)
    {
        var data = await _repo.GetAsync(_ctx.TenantId, id, ct);
        if (data is null) return NotFound();

        var (term, html) = data.Value;
        var bytes = await pdf.RenderPdfFromHtmlAsync(html, ct);

        return File(bytes, "application/pdf", $"termo_{term.TermNo:0000}.pdf");
    }

    [HttpGet("Details")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var vm = await _repo.GetAsync(_ctx.TenantId, id, ct);
        if (vm is null) return NotFound();
        return View(vm); // Views/RetentionTerm/Details.cshtml
    }
}