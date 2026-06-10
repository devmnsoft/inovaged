using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Security;
using InovaGed.Application.SystemHealth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Setup;

public sealed class SystemSeedHostedService : IHostedService
{
    private static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SystemSeedHostedService> _logger;
    private readonly ISchemaCompatibilityState _schemaState;

    public SystemSeedHostedService(IDbConnectionFactory db, ILogger<SystemSeedHostedService> logger, ISchemaCompatibilityState schemaState)
    {
        _db = db;
        _logger = logger;
        _schemaState = schemaState;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!await _schemaState.IsCompatibleAsync("SystemSeed", ct))
        {
            _logger.LogWarning("SystemSeedHostedService não iniciado: schema incompatível. Execute migrations.");
            return;
        }

        _logger.LogInformation("System Seed START");

        var adminUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001");
        var administradorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb002");
        var administradorOphirUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb003");
        var arquivistaUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb004");

        var hasher = new PasswordHasher<ApplicationUser>();
        var adminHash = hasher.HashPassword(new ApplicationUser { Id = adminUserId, TenantId = DefaultTenantId, Email = "admin@inovaged.local" }, "Admin@123");
        var administradorHash = hasher.HashPassword(new ApplicationUser { Id = administradorUserId, TenantId = DefaultTenantId, Email = "administrador@inovaged.local" }, "Administrador@123");
        var administradorOphirHash = hasher.HashPassword(new ApplicationUser { Id = administradorOphirUserId, TenantId = DefaultTenantId, Email = "administradorophir@inovaged.local" }, "Administrador@123");
        var arquivistaHash = hasher.HashPassword(new ApplicationUser { Id = arquivistaUserId, TenantId = DefaultTenantId, Email = "arquivistaophir@inovaged.local" }, "Arquivista@123");

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
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS user_name text NULL;

        INSERT INTO ged.app_user (id, tenant_id, name, user_name, email, password_hash, is_active, must_change_password, created_at)
        VALUES (@adminUserId, @tenantId, 'Administrador do Sistema', 'admin@inovaged.local', 'admin@inovaged.local', @adminHash, true, false, now())
        ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, user_name = EXCLUDED.user_name, email = EXCLUDED.email, password_hash = EXCLUDED.password_hash, is_active = EXCLUDED.is_active, must_change_password = EXCLUDED.must_change_password;

        INSERT INTO ged.app_user (id, tenant_id, name, user_name, email, password_hash, is_active, must_change_password, created_at)
        VALUES (@arquivistaUserId, @tenantId, 'Arquivista Ophir', 'arquivistaophir@inovaged.local', 'arquivistaophir@inovaged.local', @arquivistaHash, true, false, now())
        ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, user_name = EXCLUDED.user_name, email = EXCLUDED.email, password_hash = EXCLUDED.password_hash, is_active = EXCLUDED.is_active, must_change_password = EXCLUDED.must_change_password;

        INSERT INTO ged.app_user (id, tenant_id, name, user_name, email, password_hash, is_active, must_change_password, created_at)
        VALUES (@administradorUserId, @tenantId, 'Administrador', 'administrador@inovaged.local', 'administrador@inovaged.local', @administradorHash, true, false, now())
        ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, user_name = EXCLUDED.user_name, email = EXCLUDED.email, password_hash = EXCLUDED.password_hash, is_active = EXCLUDED.is_active, must_change_password = EXCLUDED.must_change_password;

        INSERT INTO ged.app_user (id, tenant_id, name, user_name, email, password_hash, is_active, must_change_password, created_at)
        VALUES (@administradorOphirUserId, @tenantId, 'Administrador Ophir', 'administradorophir@inovaged.local', 'administradorophir@inovaged.local', @administradorOphirHash, true, false, now())
        ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, user_name = EXCLUDED.user_name, email = EXCLUDED.email, password_hash = EXCLUDED.password_hash, is_active = EXCLUDED.is_active, must_change_password = EXCLUDED.must_change_password;

        UPDATE ged.app_user SET password_hash = @adminHash
        WHERE tenant_id = @tenantId AND id = @adminUserId AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');
        UPDATE ged.app_user SET password_hash = @administradorHash
        WHERE tenant_id = @tenantId AND id = @administradorUserId AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');
        UPDATE ged.app_user SET password_hash = @administradorOphirHash
        WHERE tenant_id = @tenantId AND id = @administradorOphirUserId AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');
        UPDATE ged.app_user SET password_hash = @arquivistaHash
        WHERE tenant_id = @tenantId AND id = @arquivistaUserId AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');

        INSERT INTO ged.user_role (user_id, role_id)
        SELECT @adminUserId, r.id FROM ged.app_role r WHERE r.tenant_id = @tenantId AND r.normalized_name = 'ADMIN'
        ON CONFLICT DO NOTHING;
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT @administradorUserId, r.id FROM ged.app_role r WHERE r.tenant_id = @tenantId AND r.normalized_name = 'ADMINISTRADOR'
        ON CONFLICT DO NOTHING;
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT @administradorOphirUserId, r.id FROM ged.app_role r WHERE r.tenant_id = @tenantId AND r.normalized_name = 'ADMINISTRADOROPHIR'
        ON CONFLICT DO NOTHING;
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT @arquivistaUserId, r.id FROM ged.app_role r WHERE r.tenant_id = @tenantId AND r.normalized_name = 'ARQUIVISTAOPHIR'
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
            adminHash,
            administradorHash,
            administradorOphirHash,
            arquivistaHash
        }, cancellationToken: ct));

        _logger.LogInformation("System Seed SUCCESS");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
