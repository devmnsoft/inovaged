using InovaGed.Application.Classification;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.Classification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ClassificationDashboardController : Controller
{
    private readonly IClassificationDashboardQueries _dash;
    private readonly ICurrentUser _currentUser;

    public ClassificationDashboardController(
        IClassificationDashboardQueries dash,
        ICurrentUser currentUser)
    {
        _dash = dash;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        Guid? folderId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = _currentUser.TenantId;

        // ✅ Retorna IReadOnlyList<UnclassifiedRowDto>
        var rows = await _dash.ListAsync(
            tenantId,
            folderId,
            page,
            pageSize,
            ct);

        // ✅ Total pendente (dentro do recorte retornado)
        var total = rows.Count;

        // ✅ Por pasta (agrupamento em memória)
        var byFolder = rows
            .GroupBy(x => new { x.FolderId, x.FolderName })
            .Select(g => new ClassificationDashboardVM.ByFolderVM
            {
                FolderId = (Guid)g.Key.FolderId,
                FolderName = g.Key.FolderName,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        // ✅ Mapping DTO -> ViewModel
        var vm = new ClassificationDashboardVM
        {
            FolderId = folderId,
            TotalPending = total,
            ByFolder = byFolder,
            Items = rows.Select(r => new ClassificationDashboardVM.ItemVM
            {
                Id = r.Id,
                Title = r.Title,
                FolderName = r.FolderName,
                FileName = r.FileName,
                CreatedAt = r.CreatedAt
            }).ToList(),
            Page = page,
            PageSize = pageSize
        };

        return View(vm);
    }
}
