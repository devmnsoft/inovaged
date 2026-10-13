using System.Security.Claims;
using InovaGed.Application.Identity;
using InovaGed.Application.Sectors;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.ViewComponents;

public sealed class DemaisSetoresMenuViewComponent : ViewComponent
{
    private readonly ICurrentUser _currentUser;
    private readonly IDemaisSetoresService _sectors;

    public DemaisSetoresMenuViewComponent(ICurrentUser currentUser, IDemaisSetoresService sectors)
    {
        _currentUser = currentUser;
        _sectors = sectors;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (User.Identity?.IsAuthenticated != true) return Content(string.Empty);
        var allowed = User is ClaimsPrincipal principal && principal.IsFullAdmin()
            || await _sectors.CanEnterAsync(_currentUser.TenantId, _currentUser.UserId, false, CancellationToken.None);
        return allowed ? View() : Content(string.Empty);
    }
}
