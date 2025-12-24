namespace InovaGed.Application.Auth;


public interface IAuthRepository
{
    Task<AuthUserRow?> FindUserAsync(string tenantSlug, string email, CancellationToken ct);

    Task<IReadOnlyList<string>> GetRolesAsync(Guid tenantId, Guid userId, CancellationToken ct);

    Task<string?> GetPasswordHashAsync(Guid tenantId, Guid userId, CancellationToken ct);

    Task EnsureAdminSeedPasswordAsync(Guid tenantId, Guid userId, string newHash, CancellationToken ct);
}




