using Dapper;
using InovaGed.Application.Identity;

namespace InovaGed.Application.Administration;

public sealed record PermissionEvaluationContext(Guid TenantId, Guid UserId, string PermissionCode, string? Module, string? Route, string? Action, string? CorrelationId);
public interface IRealPermissionChecker { Task<bool> IsAllowedAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct = default); }
public interface IPermissionGovernanceRepository
{
    Task<PermissionMode> GetModeAsync(Guid tenantId, CancellationToken ct = default);
    Task LogEvaluationAsync(PermissionEvaluationContext context, bool legacyAllowed, bool realAllowed, PermissionMode mode, CancellationToken ct = default);
}
public sealed class DatabasePermissionChecker(InovaGed.Application.Common.Database.IDbConnectionFactory db) : IRealPermissionChecker
{
    public async Task<bool> IsAllowedAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || string.IsNullOrWhiteSpace(permissionCode)) return false;
        await using var c = await db.OpenAsync(ct);
        const string sql = @"
select exists (
    select 1
    from ged.app_user u
    join ged.user_role ur on ur.tenant_id = u.tenant_id and ur.user_id = u.id
    join ged.app_role r on r.tenant_id = ur.tenant_id and r.id = ur.role_id
    join ged.role_permission rp on rp.tenant_id = r.tenant_id and rp.role_id = r.id
    join ged.permission p on p.tenant_id = rp.tenant_id and p.id = rp.permission_id
    where u.tenant_id = @tenantId
      and u.id = @userId
      and u.is_active = true
      and coalesce(u.is_locked, false) = false
      and u.deleted_at_utc is null
      and coalesce(ur.is_active, true) = true
      and coalesce(r.is_active, true) = true
      and coalesce(rp.is_active, true) = true
      and coalesce(p.is_active, true) = true
      and (p.code = @permissionCode or coalesce(r.is_administrator, false) = true)
);";
        return await c.ExecuteScalarAsync<bool>(new Dapper.CommandDefinition(sql, new { tenantId, userId, permissionCode }, cancellationToken: ct));
    }
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
