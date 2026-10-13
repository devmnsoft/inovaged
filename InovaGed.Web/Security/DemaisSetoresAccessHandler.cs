using InovaGed.Application.Identity;
using InovaGed.Application.Sectors;
using Microsoft.AspNetCore.Authorization;

namespace InovaGed.Web.Security;

public sealed class DemaisSetoresAccessRequirement : IAuthorizationRequirement;

public sealed class DemaisSetoresAccessHandler : AuthorizationHandler<DemaisSetoresAccessRequirement>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDemaisSetoresService _sectors;

    public DemaisSetoresAccessHandler(ICurrentUser currentUser, IDemaisSetoresService sectors)
    {
        _currentUser = currentUser;
        _sectors = sectors;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, DemaisSetoresAccessRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (context.User.IsFullAdmin() || await _sectors.CanEnterAsync(_currentUser.TenantId, _currentUser.UserId, false, CancellationToken.None))
            context.Succeed(requirement);
    }
}
