namespace InovaGed.Application.Auth;

public sealed class AuthUserRow
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ServidorId { get; set; }

    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    public bool IsActive { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public int FailedAccessCount { get; set; }
    public bool MustChangePassword { get; set; }
    public bool MfaEnabled { get; set; }
    public bool CertificateRequired { get; set; }
    public bool CanSignWithIcp { get; set; }
    public string SecurityLevel { get; set; } = "PUBLIC";
}