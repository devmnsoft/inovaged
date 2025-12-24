namespace InovaGed.Application.Identity;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid TenantId { get; }
    Guid UserId { get; }
    string Email { get; }
    IReadOnlyList<string> Roles { get; }
}
