using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Security;

public sealed class AppUser : TenantEntity
{
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    private AppUser() { }

    public AppUser(Guid tenantId, string name, string email, string passwordHash, Guid createdBy)
    {
        TenantId = tenantId;
        Name = name.Trim();
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        CreatedBy = createdBy;
    }

    public void ChangePassword(string passwordHash, Guid userId)
    {
        PasswordHash = passwordHash;
        Touch(userId);
    }

    public void Deactivate(Guid userId) { IsActive = false; Touch(userId); }
    public void Activate(Guid userId) { IsActive = true; Touch(userId); }
}
