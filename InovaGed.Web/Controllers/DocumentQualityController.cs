using InovaGed.Application.Audit;
using InovaGed.Application.DocumentQuality;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class DocumentQualityController : Controller
{
    private readonly IDocumentQualityAnalyzerService _service;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;

    public DocumentQualityController(IDocumentQualityAnalyzerService service, ICurrentUser currentUser, IAuditWriter audit)
    {
        _service = service;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] DocumentQualityFilter filter, CancellationToken ct)
    {
        if (!CanView()) return RedirectToAction("AccessDenied", "Account");
        var vm = await _service.GetDashboardAsync(_currentUser.TenantId, filter, ct);
        ViewData["CanRunDocumentQuality"] = CanRun();
        return View(vm);
    }

    [HttpGet("Run")]
    public IActionResult Run()
    {
        if (!CanRun()) return RedirectToAction("AccessDenied", "Account");
        return View();
    }

    [HttpPost("RunNow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow([FromForm] DocumentQualityFilter filter, CancellationToken ct)
    {
        if (!CanRun()) return Forbid();
        await AuditAsync("DOCUMENT_QUALITY_REANALYZE_REQUESTED", null, new { filter, mode = "all" }, ct);
        try
        {
            var result = await _service.AnalyzeAllAsync(_currentUser.TenantId, filter, ct);
            TempData["Success"] = $"Análise concluída: {result.TotalDocuments} documentos, {result.CriticalCount} críticos.";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Qualidade Documental", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Warning"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Details/{documentId:guid}")]
    public async Task<IActionResult> Details(Guid documentId, CancellationToken ct)
    {
        if (!CanView()) return RedirectToAction("AccessDenied", "Account");
        await AuditAsync("DOCUMENT_QUALITY_ACTION_OPENED", documentId, new { documentId, action = "details" }, ct);
        var history = await _service.GetHistoryAsync(_currentUser.TenantId, documentId, ct);
        try
        {
            var current = history.FirstOrDefault() ?? await _service.AnalyzeOneAsync(_currentUser.TenantId, documentId, ct);
            ViewBag.History = history;
            return View(current);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Qualidade Documental", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Warning"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("Reanalyze/{documentId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reanalyze(Guid documentId, CancellationToken ct)
    {
        if (!CanView()) return Forbid();
        await AuditAsync("DOCUMENT_QUALITY_REANALYZE_REQUESTED", documentId, new { documentId, mode = "single" }, ct);
        try
        {
            var result = await _service.AnalyzeOneAsync(_currentUser.TenantId, documentId, ct);
            TempData["Success"] = $"Documento reanalisado: score {result.QualityScore} ({result.QualityStatus}).";
            return RedirectToAction(nameof(Details), new { documentId });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Qualidade Documental", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Warning"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    private bool CanView()
        => RolePolicyHelper.IsFullAdmin(User)
           || User.IsInNormalizedRole(AppRoles.AdministradorOphir)
           || User.IsInNormalizedRole(AppRoles.ArquivistaOphir);

    private bool CanRun()
        => RolePolicyHelper.IsFullAdmin(User) || User.IsInNormalizedRole(AppRoles.AdministradorOphir);

    private Task AuditAsync(string action, Guid? documentId, object data, CancellationToken ct)
        => _audit.WriteAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            action,
            documentId.HasValue ? "DOCUMENT" : "DOCUMENT_QUALITY",
            documentId,
            "Ação na Central de Qualidade Documental",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            data,
            ct);
}
