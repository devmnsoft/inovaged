namespace InovaGed.Application.Auth;

public interface IAuthRepository
{
    Task<AuthUserRow?> FindUserAsync(
        string tenantSlug,
        string email,
        CancellationToken ct);

    Task<IReadOnlyList<string>> GetRolesAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct);

    Task<string?> GetPasswordHashAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct);

    Task EnsureAdminSeedPasswordAsync(
        Guid tenantId,
        Guid userId,
        string newHash,
        CancellationToken ct);

    Task RegisterLoginSuccessAsync(
        Guid tenantId,
        Guid userId,
        string? ip,
        string? userAgent,
        string? correlationId,
        CancellationToken ct);

    Task RegisterLoginFailureAsync(
        Guid tenantId,
        Guid userId,
        string reason,
        string? ip,
        string? userAgent,
        string? correlationId,
        CancellationToken ct);

    Task<PasswordRecoveryUserDto?> FindUserForPasswordRecoveryByCpfAsync(
        string tenantSlug,
        string cpf,
        CancellationToken ct);

    Task ResetPasswordByUserIdAsync(
        Guid tenantId,
        Guid userId,
        string newPasswordHash,
        CancellationToken ct);
}