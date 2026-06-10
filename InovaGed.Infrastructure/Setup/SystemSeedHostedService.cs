using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Security;
using InovaGed.Application.SystemHealth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.Setup;

public sealed class SystemSeedOptions
{
    public bool Enabled { get; set; } = true;
    public bool FailFastOnSeedError { get; set; }
}

public sealed class SystemSeedHostedService : IHostedService
{
    private static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SystemSeedHostedService> _logger;
    private readonly ISchemaCompatibilityState _schemaState;
    private readonly SystemSeedOptions _options;

    public SystemSeedHostedService(
        IDbConnectionFactory db,
        ILogger<SystemSeedHostedService> logger,
        ISchemaCompatibilityState schemaState,
        IOptions<SystemSeedOptions> options)
    {
        _db = db;
        _logger = logger;
        _schemaState = schemaState;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("SystemSeedHostedService desabilitado por configuração.");
            return;
        }

        if (!await _schemaState.IsCompatibleAsync("SystemSeed", ct))
        {
            _logger.LogWarning("SystemSeedHostedService não iniciado: schema incompatível. Execute migrations.");
            return;
        }

        try
        {
            await RunSeedAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogWarning(ex, "Seed ignorou registro duplicado. Constraint={ConstraintName}", ex.ConstraintName);
            if (_options.FailFastOnSeedError)
                throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no SystemSeed.");
            if (_options.FailFastOnSeedError)
                throw;
        }
    }

    private async Task RunSeedAsync(CancellationToken ct)
    {
        _logger.LogInformation("System Seed START");

        var adminUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001");
        var administradorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb002");
        var administradorOphirUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb003");
        var arquivistaUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb004");

        var adminEmail = NormalizeEmail("admin@inovaged.local");
        var administradorEmail = NormalizeEmail("administrador@inovaged.local");
        var administradorOphirEmail = NormalizeEmail("administradorophir@inovaged.local");
        var arquivistaEmail = NormalizeEmail("arquivistaophir@inovaged.local");

        var hasher = new PasswordHasher<ApplicationUser>();
        var adminHash = hasher.HashPassword(new ApplicationUser { Id = adminUserId, TenantId = DefaultTenantId, Email = adminEmail }, "Admin@123");
        var administradorHash = hasher.HashPassword(new ApplicationUser { Id = administradorUserId, TenantId = DefaultTenantId, Email = administradorEmail }, "Administrador@123");
        var administradorOphirHash = hasher.HashPassword(new ApplicationUser { Id = administradorOphirUserId, TenantId = DefaultTenantId, Email = administradorOphirEmail }, "Administrador@123");
        var arquivistaHash = hasher.HashPassword(new ApplicationUser { Id = arquivistaUserId, TenantId = DefaultTenantId, Email = arquivistaEmail }, "Arquivista@123");

        const string sql = """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;
        INSERT INTO ged.app_role (id, tenant_id, name, normalized_name, created_at)
        VALUES
        (gen_random_uuid(), @tenantId, 'ADMIN', 'ADMIN', now()),
        (gen_random_uuid(), @tenantId, 'ADMINISTRADOR', 'ADMINISTRADOR', now()),
        (gen_random_uuid(), @tenantId, 'ADMINISTRADOROPHIR', 'ADMINISTRADOROPHIR', now()),
        (gen_random_uuid(), @tenantId, 'ARQUIVISTAOPHIR', 'ARQUIVISTAOPHIR', now()),
        (gen_random_uuid(), @tenantId, 'HOSPITAL', 'HOSPITAL', now())
        ON CONFLICT (tenant_id, normalized_name) DO UPDATE SET name = EXCLUDED.name;

        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS name text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS email text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS password_hash text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT false;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS updated_at timestamptz NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS user_name text NULL;

        WITH seed_users(id, tenant_id, name, user_name, email, password_hash) AS (
            VALUES
            (@adminUserId::uuid, @tenantId::uuid, 'Administrador do Sistema', @adminEmail, @adminEmail, @adminHash),
            (@arquivistaUserId::uuid, @tenantId::uuid, 'Arquivista Ophir', @arquivistaEmail, @arquivistaEmail, @arquivistaHash),
            (@administradorUserId::uuid, @tenantId::uuid, 'Administrador', @administradorEmail, @administradorEmail, @administradorHash),
            (@administradorOphirUserId::uuid, @tenantId::uuid, 'Administrador Ophir', @administradorOphirEmail, @administradorOphirEmail, @administradorOphirHash)
        )
        UPDATE ged.app_user u
        SET name = s.name,
            user_name = s.user_name,
            email = lower(trim(s.email)),
            is_active = true,
            must_change_password = false,
            password_hash = coalesce(nullif(u.password_hash, ''), s.password_hash),
            updated_at = now()
        FROM seed_users s
        WHERE u.tenant_id = s.tenant_id
          AND lower(trim(u.email)) = lower(trim(s.email));

        INSERT INTO ged.app_user (id, tenant_id, name, user_name, email, password_hash, is_active, must_change_password, created_at)
        VALUES
        (@adminUserId, @tenantId, 'Administrador do Sistema', @adminEmail, @adminEmail, @adminHash, true, false, now()),
        (@arquivistaUserId, @tenantId, 'Arquivista Ophir', @arquivistaEmail, @arquivistaEmail, @arquivistaHash, true, false, now()),
        (@administradorUserId, @tenantId, 'Administrador', @administradorEmail, @administradorEmail, @administradorHash, true, false, now()),
        (@administradorOphirUserId, @tenantId, 'Administrador Ophir', @administradorOphirEmail, @administradorOphirEmail, @administradorOphirHash, true, false, now())
        ON CONFLICT (tenant_id, email) DO UPDATE SET
            name = EXCLUDED.name,
            user_name = EXCLUDED.user_name,
            is_active = true,
            must_change_password = false,
            password_hash = coalesce(nullif(ged.app_user.password_hash, ''), EXCLUDED.password_hash),
            updated_at = now();

        UPDATE ged.app_user SET password_hash = @adminHash
        WHERE tenant_id = @tenantId AND lower(trim(email)) = @adminEmail AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');
        UPDATE ged.app_user SET password_hash = @administradorHash
        WHERE tenant_id = @tenantId AND lower(trim(email)) = @administradorEmail AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');
        UPDATE ged.app_user SET password_hash = @administradorOphirHash
        WHERE tenant_id = @tenantId AND lower(trim(email)) = @administradorOphirEmail AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');
        UPDATE ged.app_user SET password_hash = @arquivistaHash
        WHERE tenant_id = @tenantId AND lower(trim(email)) = @arquivistaEmail AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');

        INSERT INTO ged.user_role (user_id, role_id)
        SELECT u.id, r.id FROM ged.app_user u JOIN ged.app_role r ON r.tenant_id = u.tenant_id AND r.normalized_name = 'ADMIN'
        WHERE u.tenant_id = @tenantId AND lower(trim(u.email)) = @adminEmail
        ON CONFLICT DO NOTHING;
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT u.id, r.id FROM ged.app_user u JOIN ged.app_role r ON r.tenant_id = u.tenant_id AND r.normalized_name = 'ADMINISTRADOR'
        WHERE u.tenant_id = @tenantId AND lower(trim(u.email)) = @administradorEmail
        ON CONFLICT DO NOTHING;
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT u.id, r.id FROM ged.app_user u JOIN ged.app_role r ON r.tenant_id = u.tenant_id AND r.normalized_name = 'ADMINISTRADOROPHIR'
        WHERE u.tenant_id = @tenantId AND lower(trim(u.email)) = @administradorOphirEmail
        ON CONFLICT DO NOTHING;
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT u.id, r.id FROM ged.app_user u JOIN ged.app_role r ON r.tenant_id = u.tenant_id AND r.normalized_name = 'ARQUIVISTAOPHIR'
        WHERE u.tenant_id = @tenantId AND lower(trim(u.email)) = @arquivistaEmail
        ON CONFLICT DO NOTHING;

        INSERT INTO ged.user_role (user_id, role_id)
        SELECT ur.user_id, upper_role.id
        FROM ged.user_role ur
        JOIN ged.app_role old_role ON old_role.id = ur.role_id
        JOIN ged.app_role upper_role ON upper_role.tenant_id = old_role.tenant_id AND upper_role.normalized_name = 'ADMIN'
        WHERE old_role.tenant_id = @tenantId
          AND (old_role.name = 'Admin' OR old_role.normalized_name = 'ADMIN')
        ON CONFLICT DO NOTHING;

        INSERT INTO ged.user_role (user_id, role_id)
        SELECT ur.user_id, upper_role.id
        FROM ged.user_role ur
        JOIN ged.app_role old_role ON old_role.id = ur.role_id
        JOIN ged.app_role upper_role ON upper_role.tenant_id = old_role.tenant_id AND upper_role.normalized_name = 'ADMINISTRADOR'
        WHERE old_role.tenant_id = @tenantId
          AND (old_role.name = 'Administrador' OR old_role.normalized_name = 'ADMINISTRADOR')
        ON CONFLICT DO NOTHING;
        """;

        await using var con = await _db.OpenAsync(ct);
        await con.ExecuteAsync(new CommandDefinition(sql, new
        {
            tenantId = DefaultTenantId,
            adminUserId,
            administradorUserId,
            administradorOphirUserId,
            arquivistaUserId,
            adminEmail,
            administradorEmail,
            administradorOphirEmail,
            arquivistaEmail,
            adminHash,
            administradorHash,
            administradorOphirHash,
            arquivistaHash
        }, cancellationToken: ct));

        _logger.LogInformation("System Seed SUCCESS");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
