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
    private readonly ILogger<GedSearchController> _logger;

    public GedSearchController(ICurrentUser currentUser, IGedAccessPolicyService accessPolicy, IGedSearchService service, ILogger<GedSearchController> logger)
    { _currentUser = currentUser; _accessPolicy = accessPolicy; _service = service; _logger = logger; }

    [HttpGet("")]
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
        var result = await _service.SearchAsync(filter, ct);
        return Json(new { success = true, result });
    }

    [HttpGet("Suggest")]
    public IActionResult Suggest([FromQuery] string? term)
    {
        var items = string.IsNullOrWhiteSpace(term) ? Array.Empty<object>() : new object[] { new { type = "document", id = Guid.Empty, title = term, subtitle = "Busca rápida" } };
        return Json(new { success = true, items });
    }
}
