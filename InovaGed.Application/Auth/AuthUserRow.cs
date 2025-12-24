namespace InovaGed.Application.Auth;

public sealed class AuthUserRow
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
