using Dapper;
using InovaGed.Application.Auth;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Auth;

public sealed class AuthRepository : IAuthRepository
{
    private readonly IDbConnectionFactory _factory;

    public AuthRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<AuthUserRow?> FindUserAsync(
        string tenantSlug,
        string email,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    t.id                         AS ""TenantId"",
    u.id                         AS ""UserId"",
    u.servidor_id                AS ""ServidorId"",
    u.email                      AS ""Email"",
    u.name                       AS ""Name"",
    u.password_hash              AS ""PasswordHash"",
    u.is_active                  AS ""IsActive"",
    u.is_locked                  AS ""IsLocked"",
    u.locked_until               AS ""LockedUntil"",
    u.failed_access_count        AS ""FailedAccessCount"",
    u.must_change_password       AS ""MustChangePassword"",
    u.mfa_enabled                AS ""MfaEnabled"",
    u.certificate_required       AS ""CertificateRequired"",
    u.can_sign_with_icp          AS ""CanSignWithIcp"",
    u.security_level             AS ""SecurityLevel""
FROM ged.tenant t
JOIN ged.app_user u ON u.tenant_id = t.id
LEFT JOIN ged.servidor s
       ON s.id = u.servidor_id
      AND s.tenant_id = u.tenant_id
WHERE lower(t.code) = lower(@tenantSlug)
  AND lower(u.email) = lower(@email)
  AND t.is_active = true
  AND u.deleted_at_utc IS NULL
LIMIT 1;
";

        using var conn = await _factory.OpenAsync(ct);

        return await conn.QueryFirstOrDefaultAsync<AuthUserRow>(
            new CommandDefinition(
                sql,
                new { tenantSlug, email },
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT r.normalized_name
FROM ged.user_role ur
JOIN ged.app_role r ON r.id = ur.role_id
WHERE ur.user_id = @userId;
";

        using var conn = await _factory.OpenAsync(ct);

        var roles = await conn.QueryAsync<string>(
            new CommandDefinition(
                sql,
                new { tenantId, userId },
                cancellationToken: ct));

        return roles.ToList();
    }

    public async Task<string?> GetPasswordHashAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT password_hash
FROM ged.app_user
WHERE tenant_id = @tenantId
  AND id = @userId
  AND deleted_at_utc IS NULL;
";

        using var conn = await _factory.OpenAsync(ct);

        return await conn.QueryFirstOrDefaultAsync<string?>(
            new CommandDefinition(
                sql,
                new { tenantId, userId },
                cancellationToken: ct));
    }

    public async Task EnsureAdminSeedPasswordAsync(
        Guid tenantId,
        Guid userId,
        string newHash,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET password_hash = @newHash,
    updated_at_utc = now()
WHERE tenant_id = @tenantId
  AND id = @userId
  AND deleted_at_utc IS NULL;
";

        using var conn = await _factory.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { tenantId, userId, newHash },
                cancellationToken: ct));
    }

    public async Task RegisterLoginSuccessAsync(
        Guid tenantId,
        Guid userId,
        string? ip,
        string? userAgent,
        string? correlationId,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET failed_access_count = 0,
    is_locked = false,
    locked_until = NULL,
    last_login_at = now()
WHERE tenant_id = @tenantId
  AND id = @userId;

SELECT ged.audit_user_security_event(
    @tenantId,
    @userId,
    NULL,
    'LOGIN_SUCCESS',
    'Login realizado com sucesso.',
    @userId,
    @ip,
    @userAgent,
    @correlationId,
    '{}'::jsonb
);
";

        using var conn = await _factory.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { tenantId, userId, ip, userAgent, correlationId },
                cancellationToken: ct));
    }

    public async Task RegisterLoginFailureAsync(
        Guid tenantId,
        Guid userId,
        string reason,
        string? ip,
        string? userAgent,
        string? correlationId,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET failed_access_count = failed_access_count + 1,
    is_locked = CASE WHEN failed_access_count + 1 >= 5 THEN true ELSE is_locked END,
    locked_until = CASE WHEN failed_access_count + 1 >= 5 THEN now() + interval '15 minutes' ELSE locked_until END
WHERE tenant_id = @tenantId
  AND id = @userId;

SELECT ged.audit_user_security_event(
    @tenantId,
    @userId,
    NULL,
    'LOGIN_FAILURE',
    @reason,
    @userId,
    @ip,
    @userAgent,
    @correlationId,
    jsonb_build_object('reason', @reason)
);
";

        using var conn = await _factory.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { tenantId, userId, reason, ip, userAgent, correlationId },
                cancellationToken: ct));
    }

    public async Task<PasswordRecoveryUserDto?> FindUserForPasswordRecoveryByCpfAsync(
    string tenantSlug,
    string cpf,
    CancellationToken ct)
    {
        const string sql = @"
SELECT
    t.id              AS ""TenantId"",
    u.id              AS ""UserId"",
    s.id              AS ""ServidorId"",
    COALESCE(s.nome_completo, u.name) AS ""NomeUsuario"",
    u.email           AS ""EmailUsuario"",
    s.cpf             AS ""Cpf"",
    u.is_active       AS ""IsActive""
FROM ged.tenant t
JOIN ged.servidor s
     ON s.tenant_id = t.id
    AND s.reg_status = 'A'
JOIN ged.app_user u
     ON u.tenant_id = t.id
    AND u.servidor_id = s.id
WHERE lower(t.code) = lower(@TenantSlug)
  AND regexp_replace(s.cpf, '\D', '', 'g') = regexp_replace(@Cpf, '\D', '', 'g')
  AND t.is_active = true
  AND u.deleted_at_utc IS NULL
LIMIT 1;
";

        using var conn = await _factory.OpenAsync(ct);

        return await conn.QueryFirstOrDefaultAsync<PasswordRecoveryUserDto>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantSlug = tenantSlug,
                    Cpf = cpf
                },
                cancellationToken: ct));
    }

    public async Task ResetPasswordByUserIdAsync(
        Guid tenantId,
        Guid userId,
        string newPasswordHash,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET password_hash = @PasswordHash,
    must_change_password = false,
    failed_access_count = 0,
    is_locked = false,
    locked_until = NULL,
    password_reset_token_hash = NULL,
    password_reset_expires_at = NULL,
    password_reset_used_at = now(),
    last_password_change_at = now(),
    updated_at_utc = now()
WHERE tenant_id = @TenantId
  AND id = @UserId
  AND deleted_at_utc IS NULL;

INSERT INTO ged.user_security_event (
    tenant_id,
    user_id,
    servidor_id,
    event_type,
    event_description,
    created_at,
    data
)
SELECT
    u.tenant_id,
    u.id,
    u.servidor_id,
    'PASSWORD_RESET_BY_CPF',
    'Senha redefinida por fluxo de recuperação via CPF.',
    now(),
    jsonb_build_object('source', 'ForgotPassword', 'minimum_length', 4)
FROM ged.app_user u
WHERE u.tenant_id = @TenantId
  AND u.id = @UserId;
";

        using var conn = await _factory.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    UserId = userId,
                    PasswordHash = newPasswordHash
                },
                cancellationToken: ct));
    }

   
}