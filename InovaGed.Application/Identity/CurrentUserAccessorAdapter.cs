using InovaGed.Application.Classification;
using InovaGed.Application.Identity;

public sealed class CurrentUserAccessorAdapter : ICurrentUserAccessor
{
    private readonly ICurrentUser _currentUser;

    public CurrentUserAccessorAdapter(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public Guid TenantId => _currentUser.TenantId;
    public Guid? UserId => _currentUser.UserId;
}