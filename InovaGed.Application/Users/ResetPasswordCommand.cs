namespace InovaGed.Application.Users;

public sealed class ResetPasswordCommand
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool MustChangePassword { get; set; } = true;
    public Guid? ChangedBy { get; set; }
}