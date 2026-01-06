using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Users;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Users;

public sealed class UserAdminRepository : IUserAdminRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<UserAdminRepository> _logger;

    public UserAdminRepository(IDbConnectionFactory db, ILogger<UserAdminRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RoleRowDto>> ListRolesAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT id AS ""Id"", name AS ""Name""
FROM ged.roles
WHERE tenant_id = @TenantId
ORDER BY lower(name);
";
        try
        {
            await using var con = await _db.OpenAsync(ct);
            var rows = await con.QueryAsync<RoleRowDto>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));
            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ListRolesAsync | Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<Guid> CreateUserAsync(
        Guid tenantId,
        string name,
        string email,
        string passwordHash,
        bool isActive,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct)
    {
        const string insertUserSql = @"
INSERT INTO ged.app_user
(id, tenant_id, name, email, password_hash, is_active, created_at)
VALUES
(@Id, @TenantId, @Name, @Email, @PasswordHash, @IsActive, now());
";

        const string insertRoleSql = @"
INSERT INTO ged.user_roles (tenant_id, user_id, role_id)
VALUES (@TenantId, @UserId, @RoleId);
";

        try
        {
            await using var con = await _db.OpenAsync(ct);
            await using var tx = con.BeginTransaction();

            var userId = Guid.NewGuid();

            await con.ExecuteAsync(new CommandDefinition(
                insertUserSql,
                new
                {
                    Id = userId,
                    TenantId = tenantId,
                    Name = name,
                    Email = email,
                    PasswordHash = passwordHash,
                    IsActive = isActive
                },
                transaction: tx,
                cancellationToken: ct));

            var distinctRoles = (roleIds ?? Array.Empty<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            foreach (var roleId in distinctRoles)
            {
                await con.ExecuteAsync(new CommandDefinition(
                    insertRoleSql,
                    new { TenantId = tenantId, UserId = userId, RoleId = roleId },
                    transaction: tx,
                    cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
            return userId;
        }
        catch (PostgresException pex) when (pex.SqlState == "23505")
        {
            _logger.LogWarning(pex, "E-mail duplicado ao criar usuário | Tenant={TenantId} Email={Email}", tenantId, email);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro CreateUserAsync | Tenant={TenantId} Email={Email}", tenantId, email);
            throw;
        }
    }

    public async Task SetActiveAsync(Guid tenantId, Guid userId, bool isActive, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET is_active = @IsActive
WHERE tenant_id = @TenantId
  AND id = @UserId;
";
        try
        {
            await using var con = await _db.OpenAsync(ct);
            await con.ExecuteAsync(new CommandDefinition(sql,
                new { TenantId = tenantId, UserId = userId, IsActive = isActive },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro SetActiveAsync | Tenant={TenantId} User={UserId}", tenantId, userId);
            throw;
        }
    }

    public async Task ResetPasswordAsync(Guid tenantId, Guid userId, string newPasswordHash, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET password_hash = @Hash
WHERE tenant_id = @TenantId
  AND id = @UserId;
";
        try
        {
            await using var con = await _db.OpenAsync(ct);
            await con.ExecuteAsync(new CommandDefinition(sql,
                new { TenantId = tenantId, UserId = userId, Hash = newPasswordHash },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ResetPasswordAsync | Tenant={TenantId} User={UserId}", tenantId, userId);
            throw;
        }
    }
}
