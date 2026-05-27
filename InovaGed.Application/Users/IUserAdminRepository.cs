using InovaGed.Infrastructure.Users;

namespace InovaGed.Application.Users;

public interface IUserAdminRepository
{
    Task<IReadOnlyList<RoleRowDto>> ListRolesAsync(Guid tenantId, CancellationToken ct);

    Task<ServidorUsuarioCreatedDto> CreateServidorUsuarioAsync(
        Guid tenantId,
        CreateServidorUsuarioCommand command,
        CancellationToken ct);

    Task<UserEditDto?> GetForEditByServidorIdAsync(
        Guid tenantId,
        Guid servidorId,
        bool isAdmin,
        CancellationToken ct);

    Task<(bool ServidorExiste, bool UsuarioExiste)> GetEditDiagnosticAsync(
        Guid tenantId,
        Guid servidorId,
        CancellationToken ct);

    Task UpdateServidorUsuarioAsync(
        Guid tenantId,
        UpdateServidorUsuarioCommand command,
        CancellationToken ct);

    Task SetActiveAsync(Guid tenantId, Guid userId, bool isActive, Guid? changedBy, CancellationToken ct);

    Task ResetPasswordAsync(
        Guid tenantId,
        Guid userId,
        string newPasswordHash,
        bool mustChangePassword,
        Guid? changedBy,
        CancellationToken ct);

    Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? ignoreUserId, CancellationToken ct);

    Task<bool> CpfExistsAsync(Guid tenantId, string? cpf, Guid? ignoreServidorId, CancellationToken ct);

    Task<bool> UnlockUserAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
