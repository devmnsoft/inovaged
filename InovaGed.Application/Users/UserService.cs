using InovaGed.Application.Audit;
using InovaGed.Application.Identity;

namespace InovaGed.Application.Users;

public sealed class UserService
{
    private const string AdministratorRole = "ADMINISTRATOR";

    private readonly ICurrentUser _currentUser;
    private readonly IUserAdminRepository _userRepository;
    private readonly IAuditWriter _auditWriter;

    public UserService(
        ICurrentUser currentUser,
        IUserAdminRepository userRepository,
        IAuditWriter auditWriter)
    {
        _currentUser = currentUser;
        _userRepository = userRepository;
        _auditWriter = auditWriter;
    }

    public async Task<bool> UnlockUserAsync(Guid userId, string? ipAddress, string? userAgent, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.Roles.Any(r => string.Equals(r, AdministratorRole, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Somente ADMINISTRATOR pode desbloquear usuários.");

        var unlocked = await _userRepository.UnlockUserAsync(_currentUser.TenantId, userId, ct);
        if (!unlocked)
            return false;

        await _auditWriter.WriteAsync(
            tenantId: _currentUser.TenantId,
            userId: _currentUser.UserId,
            action: "UPDATE",
            entityName: "APP_USER",
            entityId: userId,
            summary: $"ADMIN_UNLOCK_USER | actor={_currentUser.UserId} | target={userId}",
            ipAddress: ipAddress,
            userAgent: userAgent,
            data: new
            {
                actionType = "UNLOCK_USER",
                actorUserId = _currentUser.UserId,
                unlockedUserId = userId,
                timestamp = DateTimeOffset.UtcNow
            },
            ct: ct);

        return true;
    }
}
