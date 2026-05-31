using System.Diagnostics;
using InovaGed.Application.Audit;
using InovaGed.Application.Ged.Search;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Ged/Search")]
public sealed class GedSearchController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IGedAccessPolicyService _accessPolicy;
    private readonly IGedSearchService _service;
    private readonly IGedSmartSearchService _smartSearch;
    private readonly IAuditWriter _audit;
    private readonly ILogger<GedSearchController> _logger;

    public GedSearchController(ICurrentUser currentUser, IGedAccessPolicyService accessPolicy, IGedSearchService service, IGedSmartSearchService smartSearch, IAuditWriter audit, ILogger<GedSearchController> logger)
    { _currentUser = currentUser; _accessPolicy = accessPolicy; _service = service; _smartSearch = smartSearch; _audit = audit; _logger = logger; }

    [HttpGet("Advanced")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return RedirectToAction("Login", "Account");
        if (!await _accessPolicy.CanAccessGedAsync(_currentUser.TenantId, _currentUser.UserId, User, ct)) return Forbid();
        return View("~/InovaGed.Web/Views/GedSearch/Index.cshtml");
    }

    [HttpPost("Results")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Results([FromForm] GedSearchFilter filter, CancellationToken ct)
    {
        filter.TenantId = _currentUser.TenantId; filter.UserId = _currentUser.UserId;
        var sw = Stopwatch.StartNew();
        var result = await _service.SearchAsync(filter, ct);
        sw.Stop();
        _logger.LogInformation("GED advanced search executado. Tenant={TenantId} User={UserId} Query={Query} FolderId={FolderId} ResultCount={ResultCount} ElapsedMs={ElapsedMs} Module={Module} CorrelationId={CorrelationId}", filter.TenantId, filter.UserId, filter.Term, filter.FolderId, result.Total, sw.ElapsedMilliseconds, "GED", HttpContext.TraceIdentifier);
        await _audit.WriteAsync(filter.TenantId, filter.UserId, "VIEW", "GED_SEARCH", null, "Busca avançada GED executada", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { query = filter.Term, filter.FolderId, resultCount = result.Total, module = "GED", correlationId = HttpContext.TraceIdentifier }, ct);
        return Json(new { success = true, result });
    }

    [HttpGet("Suggest")]
    public Task<IActionResult> Suggest([FromQuery] string? term, CancellationToken ct) => Suggestions(term, null, "folder", ct);

    [HttpGet("Suggestions")]
    public async Task<IActionResult> Suggestions([FromQuery] string? q, [FromQuery] Guid? folderId, [FromQuery] string? scope, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, items = Array.Empty<object>() });
        if (!await _accessPolicy.CanAccessGedAsync(_currentUser.TenantId, _currentUser.UserId, User, ct)) return Forbid();

        var correlationId = HttpContext.TraceIdentifier;
        try
        {
            var request = new SmartSearchRequest
            {
                TenantId = _currentUser.TenantId,
                UserId = _currentUser.UserId,
                Query = q ?? string.Empty,
                FolderId = folderId,
                Scope = string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase) ? "global" : "folder",
                Module = "GED",
                Limit = 16,
                IsAdmin = User.IsInRole("ADMIN") || _currentUser.Roles.Any(r => string.Equals(r, "ADMIN", StringComparison.OrdinalIgnoreCase))
            };

            var items = await _smartSearch.SuggestAsync(request, ct);
            return Json(new { success = true, items });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em /Ged/Search/Suggestions. Tenant={TenantId} User={UserId} Query={Query} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, q, correlationId);
            return Json(new { success = false, message = "Não foi possível carregar sugestões.", correlationId, items = Array.Empty<object>() });
        }
    }
}
