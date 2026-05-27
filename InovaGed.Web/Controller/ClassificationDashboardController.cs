using InovaGed.Application.Auth;
using InovaGed.Application.Classification;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.Classification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ClassificationDashboardController : Controller
{
    private readonly IClassificationDashboardQueries _dash;
    private readonly ICurrentUser _currentUser;
    private readonly IMemoryCache _cache;

    public ClassificationDashboardController(IClassificationDashboardQueries dash, ICurrentUser currentUser, IMemoryCache cache)
    {
        _dash = dash;
        _currentUser = currentUser;
        _cache = cache;
    }

    [HttpGet("/ClassificationDashboard")]
    public async Task<IActionResult> Index(Guid? folderId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        ViewData["Title"] = "Dashboard de Classificação";
        ViewData["Subtitle"] = "Pendências de classificação (sem tipo definido).";

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;

        var tenantId = _currentUser.TenantId;

        var total = await _dash.CountAsync(tenantId, folderId, ct);
        var byFolder = await _dash.ByFolderAsync(tenantId, ct);
        var rows = await _dash.ListAsync(tenantId, folderId, page, pageSize, ct);

        var vm = new ClassificationDashboardVM
        {
            FolderId = folderId,
            TotalPending = total,
            ByFolder = byFolder.ToList(),
            Items = rows.ToList(),
            Page = page,
            PageSize = pageSize
        };

        return View(vm);
    }

    // Badge do menu
    [HttpGet("/ClassificationDashboard/Count")]
    public async Task<IActionResult> Count(Guid? folderId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var cacheKey = $"classification:count:{tenantId}:{folderId}";
        var total = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await _dash.CountAsync(tenantId, folderId, ct);
        });

        return Json(new { total, cached = true });
    }
}
