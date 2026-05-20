namespace InovaGed.Application.Common.Security;

public sealed class ApplicationUser
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string? Email { get; init; }
}
