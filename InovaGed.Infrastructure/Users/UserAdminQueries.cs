using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Users;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Users;

public sealed class UserAdminQueries : IUserAdminQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<UserAdminQueries> _logger;

    public UserAdminQueries(IDbConnectionFactory db, ILogger<UserAdminQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<UserRowDto>> ListUsersAsync(
        Guid tenantId, string? q, bool? active, int page, int pageSize, CancellationToken ct)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize is < 5 or > 100 ? 10 : pageSize;
        var offset = (page - 1) * pageSize;

        var qNorm = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        const string countSql = @"
SELECT count(*)::int
FROM ged.app_user u
WHERE u.tenant_id = @TenantId
  AND (@Active IS NULL OR u.is_active = @Active)
  AND (
       @Q IS NULL
    OR u.name ILIKE '%' || @Q || '%'
    OR u.email ILIKE '%' || @Q || '%'
  );
";

        const string listSql = @"
SELECT
    u.id         AS ""Id"",
    u.name       AS ""Name"",
    u.email      AS ""Email"",
    u.is_active  AS ""IsActive"",
    u.created_at AS ""CreatedAt"",
    COALESCE((
        SELECT string_agg(r.name, ', ' ORDER BY lower(r.name))
        FROM ged.user_roles ur
        JOIN ged.roles r
          ON r.tenant_id = ur.tenant_id AND r.id = ur.role_id
        WHERE ur.tenant_id = u.tenant_id AND ur.user_id = u.id
    ), '') AS ""RolesCsv""
FROM ged.app_user u
WHERE u.tenant_id = @TenantId
  AND (@Active IS NULL OR u.is_active = @Active)
  AND (
       @Q IS NULL
    OR u.name ILIKE '%' || @Q || '%'
    OR u.email ILIKE '%' || @Q || '%'
  )
ORDER BY lower(u.name)
OFFSET @Offset LIMIT @PageSize;
";

        try
        {
            await using var con = await _db.OpenAsync(ct);

            var total = await con.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    countSql,
                    new { TenantId = tenantId, Q = qNorm, Active = active },
                    cancellationToken: ct));

            var items = (await con.QueryAsync<UserRowDto>(
                new CommandDefinition(
                    listSql,
                    new { TenantId = tenantId, Q = qNorm, Active = active, Offset = offset, PageSize = pageSize },
                    cancellationToken: ct))).ToList();

            return new PagedResult<UserRowDto>
            {
                Total = total,
                Items = items
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ListUsersAsync | Tenant={TenantId}", tenantId);
            throw;
        }
    }
}
