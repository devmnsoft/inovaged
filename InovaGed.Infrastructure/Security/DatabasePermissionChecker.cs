using Dapper;
using InovaGed.Application.Administration;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Security;

public sealed class DatabasePermissionChecker(IDbConnectionFactory db) : IRealPermissionChecker
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
        return await c.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId, userId, permissionCode }, cancellationToken: ct));
    }
}
