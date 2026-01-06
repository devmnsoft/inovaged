namespace InovaGed.Application.Users;

public interface IUserAdminRepository
{
    Task<IReadOnlyList<RoleRowDto>> ListRolesAsync(Guid tenantId, CancellationToken ct);

    Task<Guid> CreateUserAsync(
        Guid tenantId,
        string name,
        string email,
        string passwordHash,
        bool isActive,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct);

    Task SetActiveAsync(Guid tenantId, Guid userId, bool isActive, CancellationToken ct);

    Task ResetPasswordAsync(Guid tenantId, Guid userId, string newPasswordHash, CancellationToken ct);
}
