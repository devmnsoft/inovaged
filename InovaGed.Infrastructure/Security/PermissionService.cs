using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Security
{
    public sealed class PermissionService : IPermissionService
    {
        private readonly IDbConnectionFactory _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PermissionService> _logger;

        public PermissionService(IDbConnectionFactory db, IMemoryCache cache, ILogger<PermissionService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        public async Task<bool> HasAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct)
        {
            var key = $"perm:{tenantId}:{userId}:{permissionCode}";
            if (_cache.TryGetValue(key, out bool ok)) return ok;

            try
            {
                await using var con = await _db.OpenAsync(ct);
                var roleIds = await UserRoleReader.RoleIdsAsync(con, null, tenantId, userId, ct);

                var sql = """
            SELECT 1
            FROM ged.role_permission rp
            WHERE rp.tenant_id=@TenantId AND rp.role_id=any(@RoleIds) AND rp.reg_status='A'
              AND rp.permission_code=@Perm
            LIMIT 1;
            """;

                ok = (await con.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(sql, new { TenantId = tenantId, RoleIds = roleIds, Perm = permissionCode }, cancellationToken: ct))) == 1;

                _cache.Set(key, ok, TimeSpan.FromMinutes(5));
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PermissionService.HasAsync failed Tenant={Tenant} User={User} Perm={Perm}", tenantId, userId, permissionCode);
                return false;
            }
        }
    }
}
