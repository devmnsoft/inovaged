using Dapper;
using InovaGed.Application.Auth;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Auth;

public sealed class AuthRepository : IAuthRepository
{
    private readonly IDbConnectionFactory _factory;
    public AuthRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<AuthUserRow?> FindUserAsync(string tenantSlug, string email, CancellationToken ct)
    {
        const string sql = @"
SELECT
    t.id            AS TenantId,
    u.id            AS UserId,
    u.email         AS Email,
    u.name          AS Name,
    u.password_hash AS PasswordHash,
    u.is_active     AS IsActive
FROM ged.tenant t
JOIN ged.app_user u ON u.tenant_id = t.id
WHERE lower(t.code) = lower(@tenantSlug)
  AND lower(u.email) = lower(@email)
  AND t.is_active = true
  AND u.is_active = true
LIMIT 1;";

        using var conn = await _factory.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<AuthUserRow>(
            new CommandDefinition(sql, new { tenantSlug, email }, cancellationToken: ct));
    }


    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        const string sql = @"
SELECT r.normalized_name
FROM ged.user_role ur
JOIN ged.app_role r ON r.id = ur.role_id
WHERE 1=1
  AND ur.user_id = @userId;";

        using var conn = await _factory.OpenAsync(ct);

        var roles = await conn.QueryAsync<string>(
            new CommandDefinition(sql, new { tenantId, userId }, cancellationToken: ct));

        return roles.ToList();
    }


    public async Task<string?> GetPasswordHashAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        const string sql = @"
SELECT password_hash
FROM ged.app_user
WHERE 1=1
  AND id=@userId
  AND deleted_at_utc IS NULL;";

        using var conn = await _factory.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<string?>(
            new CommandDefinition(sql, new { tenantId, userId }, cancellationToken: ct));
    }

    public async Task EnsureAdminSeedPasswordAsync(Guid tenantId, Guid userId, string newHash, CancellationToken ct)
    {
    
        const string sql = @"
UPDATE ged.app_user
SET password_hash=@newHash,
    updated_at_utc=NOW()
WHERE 1=1
  AND ;";

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, userId, newHash }, cancellationToken: ct));
    }
}
