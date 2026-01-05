using InovaGed.Application.Classification;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.ViewComponents;

public sealed class ClassificationPendingBadgeViewComponent : ViewComponent
{
    private readonly ICurrentUser _currentUser;
    private readonly IClassificationPendingCounter _counter;

    public ClassificationPendingBadgeViewComponent(ICurrentUser currentUser, IClassificationPendingCounter counter)
    {
        _currentUser = currentUser;
        _counter = counter;
    }

    public async Task<IViewComponentResult> InvokeAsync(CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Content(string.Empty);

        var total = await _counter.CountPendingAsync(_currentUser.TenantId, ct);

        if (total <= 0) return Content(string.Empty);

        // badge pequeno e discreto (no seu estilo)
        return Content($@"<span class=""badge rounded-pill text-bg-warning"" title=""Pendências de classificação"">{total}</span>");
    }
}
