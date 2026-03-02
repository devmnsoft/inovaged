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



    public RetentionTermController(IRetentionTermRepository repo, ILogger<RetentionTermController> logger, ICurrentContext ctx, IDispositionReportsQueries reports)
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

        // ✅ aqui é o módulo (RetentionTermRow)
        var rows = await _repo.ListAsync(_ctx.TenantId, from, to, status, ct);

        return View(rows); // Views/RetentionTerm/Index.cshtml
    }

    // GET /RetentionTerms/CreateFromCase?caseId=...
    [HttpGet("CreateFromCase")]
    public async Task<IActionResult> CreateFromCase(Guid caseId, CancellationToken ct)
    {
        try
        {
            // ✅ A assinatura recebe CreateTermRequest (não recebe caseId direto)
            var req = new CreateTermRequest
            {
                CaseId = caseId,
                TermType = "ELIMINATION", // ou "TRANSFER" / "COLLECTION"
                Notes = $"Gerado via fluxo Temporalidade em {DateTimeOffset.Now:dd/MM/yyyy HH:mm}."
            };

            var termId = await _repo.CreateFromCaseAsync(
                tenantId: _ctx.TenantId,
                userId: _ctx.UserId,
                req: req,
                ct: ct);

            TempData["Ok"] = "Termo criado com sucesso.";
            return RedirectToAction("Details", new { id = termId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateFromCase failed Tenant={Tenant} Case={Case}", _ctx.TenantId, caseId);
            TempData["Err"] = "Falha ao criar termo a partir do caso.";
            return Redirect("/Temporalidade");
        }
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
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost("ExecuteFinal")]
    [Authorize(Policy = Policies.CanExecuteFinal)]
    public async Task<IActionResult> ExecuteFinal(Guid termId, CancellationToken ct)
    {
        try
        {
            await _repo.ExecuteFinalAsync(_ctx.TenantId, _ctx.UserId, termId, ct);
            TempData["Success"] = "Execução final concluída.";
            return RedirectToAction("Details", new { id = termId });
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("Details", new { id = termId });
        }
    }

    [HttpGet("Pdf")]
    [Authorize(Policy = Policies.CanViewRetention)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct, [FromServices] ITermPdfGenerator pdf)
    {
        var data = await _repo.GetAsync(_ctx.TenantId, id, ct);
        if (data is null) return NotFound();

        var (term, html) = data.Value;
        var bytes = await pdf.RenderPdfFromHtmlAsync(html, ct);

        return File(bytes, "application/pdf", $"termo_{term.TermNo:0000}.pdf");
    }
     
    
    // GET /RetentionTerms/Details?id=...
    [HttpGet("Details")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var vm = await _repo.GetAsync(_ctx.TenantId, id, ct);
        if (vm is null) return NotFound();
        return View(vm);
    }
}
 