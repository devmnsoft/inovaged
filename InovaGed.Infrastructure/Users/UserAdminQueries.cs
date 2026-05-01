using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Users;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Users;

public sealed class UserAdminQueries : IUserAdminQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<UserAdminQueries> _logger;

    public UserAdminQueries(
        IDbConnectionFactory db,
        ILogger<UserAdminQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<UserRowDto>> ListUsersAsync(
        Guid tenantId,
        string? q,
        bool? active,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize is < 5 or > 100 ? 10 : pageSize;
        var offset = (page - 1) * pageSize;

        var qNorm = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        const string countSql = @"
SELECT count(*)::int
FROM ged.vw_user_admin_list u
WHERE u.tenant_id = @TenantId
  AND (@Active IS NULL OR u.is_active = @Active)
  AND (
       @Q IS NULL
    OR u.nome_completo ILIKE '%' || @Q || '%'
    OR u.email ILIKE '%' || @Q || '%'
    OR u.cpf ILIKE '%' || @Q || '%'
    OR u.matricula ILIKE '%' || @Q || '%'
    OR u.setor ILIKE '%' || @Q || '%'
    OR u.cargo ILIKE '%' || @Q || '%'
  );
";

        const string listSql = @"
SELECT
    u.user_id              AS ""Id"",
    u.servidor_id          AS ""ServidorId"",
    u.nome_completo        AS ""Name"",
    u.cpf                  AS ""Cpf"",
    u.matricula            AS ""Matricula"",
    u.cargo                AS ""Cargo"",
    u.funcao               AS ""Funcao"",
    u.setor                AS ""Setor"",
    u.lotacao              AS ""Lotacao"",
    u.unidade              AS ""Unidade"",
    u.email                AS ""Email"",
    u.is_active            AS ""IsActive"",
    u.is_locked            AS ""IsLocked"",
    u.must_change_password AS ""MustChangePassword"",
    u.mfa_enabled          AS ""MfaEnabled"",
    u.certificate_required AS ""CertificateRequired"",
    u.can_sign_with_icp    AS ""CanSignWithIcp"",
    u.security_level       AS ""SecurityLevel"",
    u.last_login_at        AS ""LastLoginAt"",
    u.created_at           AS ""CreatedAt"",
    u.roles_csv            AS ""RolesCsv""
FROM ged.vw_user_admin_list u
WHERE u.tenant_id = @TenantId
  AND (@Active IS NULL OR u.is_active = @Active)
  AND (
       @Q IS NULL
    OR u.nome_completo ILIKE '%' || @Q || '%'
    OR u.email ILIKE '%' || @Q || '%'
    OR u.cpf ILIKE '%' || @Q || '%'
    OR u.matricula ILIKE '%' || @Q || '%'
    OR u.setor ILIKE '%' || @Q || '%'
    OR u.cargo ILIKE '%' || @Q || '%'
  )
ORDER BY lower(u.nome_completo)
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
                    new
                    {
                        TenantId = tenantId,
                        Q = qNorm,
                        Active = active,
                        Offset = offset,
                        PageSize = pageSize
                    },
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