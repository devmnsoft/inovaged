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
    join ged.user_role ur on ur.user_id = u.id
    join ged.app_role r on r.id = ur.role_id and r.tenant_id = u.tenant_id
    join ged.role_permission rp on rp.tenant_id = u.tenant_id and rp.role_id = r.id and rp.reg_status = 'A'
    join ged.permission p on p.code = rp.permission_code
    where u.tenant_id = @tenantId
      and u.id = @userId
      and u.is_active = true
      and coalesce(u.is_locked, false) = false
      and u.deleted_at_utc is null
      and p.code = @permissionCode
);";
        return await c.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId, userId, permissionCode }, cancellationToken: ct));
    }
}
