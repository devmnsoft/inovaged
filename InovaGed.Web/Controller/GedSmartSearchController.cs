using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using InovaGed.Application.SmartSearch;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.HospitalDocumentsAccess)]
[Route("Ged/Search")]
public sealed class GedSmartSearchController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IGedAccessPolicyService _accessPolicy;
    private readonly ISmartSearchService _smartSearch;
    private readonly ISmartSearchRepository _repository;
    private readonly ISearchStatisticsService _statistics;
    private readonly IAuditWriter _audit;

    public GedSmartSearchController(
        ICurrentUser currentUser,
        IGedAccessPolicyService accessPolicy,
        ISmartSearchService smartSearch,
        ISmartSearchRepository repository,
        ISearchStatisticsService statistics,
        IAuditWriter audit)
    {
        _currentUser = currentUser;
        _accessPolicy = accessPolicy;
        _smartSearch = smartSearch;
        _repository = repository;
        _statistics = statistics;
        _audit = audit;
    }

    [HttpPost("Smart")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Smart([FromBody] GedSmartSearchPostRequest? body, [FromForm] string? query, [FromForm] Guid? folderId, [FromForm] string? scope, [FromForm] int pageSize = 20, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, message = "Sessão expirada." });
        if (!await _accessPolicy.CanAccessGedAsync(_currentUser.TenantId, _currentUser.UserId, User, ct)) return Forbid();

        var result = await _smartSearch.SearchAsync(new SmartSearchRequest
        {
            TenantId = _currentUser.TenantId,
            UserId = _currentUser.UserId,
            Query = body?.Query ?? query ?? string.Empty,
            FolderId = string.Equals(body?.Scope ?? scope, "global", StringComparison.OrdinalIgnoreCase) ? null : body?.FolderId ?? folderId,
            PageSize = body?.PageSize > 0 ? body.PageSize : pageSize,
            IsAdmin = RolePolicyHelper.IsFullAdmin(User),
            Source = "GED_SMART_SEARCH"
        }, ct);

        var auditEvent = result.Total == 0 ? "GED_SMART_SEARCH_NO_RESULT" : "GED_SMART_SEARCH";
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", auditEvent, null, "Busca inteligente GED executada", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { queryHashOnly = true, result.Total, result.DurationMs, correlationId = HttpContext.TraceIdentifier }, ct);
        return Json(new { success = true, result, items = result.Items });
    }

    [HttpGet("Stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return RedirectToAction("Login", "Account");
        if (!RolePolicyHelper.IsFullAdmin(User)) return Forbid();
        var model = await _statistics.GetAsync(_currentUser.TenantId, ct);
        return View("~/InovaGed.Web/Views/SmartSearch/Statistics.cshtml", model);
    }

    [HttpPost("AuditClick")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AuditClick([FromForm] Guid documentId, [FromForm] string? action, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false });
        if (!await _accessPolicy.CanAccessGedAsync(_currentUser.TenantId, _currentUser.UserId, User, ct)) return Forbid();
        var safeAction = string.IsNullOrWhiteSpace(action) ? "GED_SMART_SEARCH_CLICK" : action.Trim()[..Math.Min(action.Trim().Length, 80)];
        await _repository.LogAccessAsync(_currentUser.TenantId, _currentUser.UserId, documentId, "SMART_SEARCH", safeAction, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "GED_SMART_SEARCH_CLICK", documentId, "Clique em resultado da busca inteligente", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { action = safeAction, correlationId = HttpContext.TraceIdentifier }, ct);
        return Json(new { success = true });
    }
}

public sealed class GedSmartSearchPostRequest
{
    public string? Query { get; set; }
    public Guid? FolderId { get; set; }
    public string? Scope { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
