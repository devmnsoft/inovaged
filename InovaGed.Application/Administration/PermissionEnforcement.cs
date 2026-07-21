using InovaGed.Application.Identity;

namespace InovaGed.Application.Administration;

public sealed record PermissionEvaluationContext(Guid TenantId, Guid UserId, string PermissionCode, string? Module, string? Route, string? Action, string? CorrelationId);
public interface IRealPermissionChecker { Task<bool> IsAllowedAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct = default); }
public interface IPermissionGovernanceRepository
{
    Task<PermissionMode> GetModeAsync(Guid tenantId, CancellationToken ct = default);
    Task LogEvaluationAsync(PermissionEvaluationContext context, bool legacyAllowed, bool realAllowed, PermissionMode mode, CancellationToken ct = default);
}
public sealed class DatabasePermissionChecker : IRealPermissionChecker
{
    public Task<bool> IsAllowedAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct = default) => Task.FromResult(false);
}
public sealed class CompositePermissionChecker : IPermissionChecker
{
    private readonly IRealPermissionChecker _real;
    private readonly IPermissionGovernanceRepository _repo;
    public CompositePermissionChecker(IRealPermissionChecker real, IPermissionGovernanceRepository repo) { _real = real; _repo = repo; }
    public async Task<bool> IsAllowedAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct = default)
    {
        var mode = await _repo.GetModeAsync(tenantId, ct);
        var legacyAllowed = true;
        if (mode == PermissionMode.LEGACY) return legacyAllowed;
        var realAllowed = await _real.IsAllowedAsync(tenantId, userId, permissionCode, ct);
        if (legacyAllowed != realAllowed) await _repo.LogEvaluationAsync(new(tenantId, userId, permissionCode, null, null, null, null), legacyAllowed, realAllowed, mode, ct);
        return mode == PermissionMode.ENFORCED ? realAllowed : legacyAllowed;
    }
}
