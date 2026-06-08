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
        var arquivistaUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb002");
        var administradorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb003");

        var hasher = new PasswordHasher<ApplicationUser>();
        var adminHash = hasher.HashPassword(new ApplicationUser { Id = adminUserId, TenantId = DefaultTenantId, Email = "admin@local" }, "Admin@123");
        var arquivistaHash = hasher.HashPassword(new ApplicationUser { Id = arquivistaUserId, TenantId = DefaultTenantId, Email = "arquivista.ophir@local" }, "Arquivista@123");
        var administradorHash = hasher.HashPassword(new ApplicationUser { Id = administradorUserId, TenantId = DefaultTenantId, Email = "administrador.ophir@local" }, "Administrador@123");

        const string sql = """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;
        INSERT INTO ged.app_role (id, tenant_id, name, normalized_name, created_at)
        VALUES
        (gen_random_uuid(), @tenantId, 'ADMIN', 'ADMIN', now()),
        (gen_random_uuid(), @tenantId, 'ArquivistaOphir', 'ARQUIVISTAOPHIR', now()),
        (gen_random_uuid(), @tenantId, 'AdministradorOphir', 'ADMINISTRADOROPHIR', now())
        ON CONFLICT (tenant_id, normalized_name) DO UPDATE SET name = EXCLUDED.name;

        INSERT INTO ged.app_user (id, tenant_id, name, email, password_hash, is_active, must_change_password, created_at)
        VALUES (@adminUserId, @tenantId, 'Admin', 'admin@local', @adminHash, true, false, now())
        ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, email = EXCLUDED.email, password_hash = EXCLUDED.password_hash, is_active = EXCLUDED.is_active, must_change_password = EXCLUDED.must_change_password;

        INSERT INTO ged.app_user (id, tenant_id, name, email, password_hash, is_active, must_change_password, created_at)
        VALUES (@arquivistaUserId, @tenantId, 'Arquivista Ophir', 'arquivista.ophir@local', @arquivistaHash, true, false, now())
        ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, email = EXCLUDED.email, password_hash = EXCLUDED.password_hash, is_active = EXCLUDED.is_active, must_change_password = EXCLUDED.must_change_password;

        INSERT INTO ged.app_user (id, tenant_id, name, email, password_hash, is_active, must_change_password, created_at)
        VALUES (@administradorUserId, @tenantId, 'Administrador Ophir', 'administrador.ophir@local', @administradorHash, true, false, now())
        ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, email = EXCLUDED.email, password_hash = EXCLUDED.password_hash, is_active = EXCLUDED.is_active, must_change_password = EXCLUDED.must_change_password;

        UPDATE ged.app_user SET password_hash = @adminHash
        WHERE tenant_id = @tenantId AND id = @adminUserId AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');
        UPDATE ged.app_user SET password_hash = @arquivistaHash
        WHERE tenant_id = @tenantId AND id = @arquivistaUserId AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');
        UPDATE ged.app_user SET password_hash = @administradorHash
        WHERE tenant_id = @tenantId AND id = @administradorUserId AND (password_hash IS NULL OR password_hash = '' OR password_hash !~ '^AQAAAA');

        INSERT INTO ged.user_role (user_id, role_id)
        SELECT @adminUserId, r.id FROM ged.app_role r WHERE r.tenant_id = @tenantId AND r.normalized_name = 'ADMIN'
        ON CONFLICT DO NOTHING;
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT @arquivistaUserId, r.id FROM ged.app_role r WHERE r.tenant_id = @tenantId AND r.normalized_name = 'ARQUIVISTAOPHIR'
        ON CONFLICT DO NOTHING;
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT @administradorUserId, r.id FROM ged.app_role r WHERE r.tenant_id = @tenantId AND r.normalized_name = 'ADMINISTRADOROPHIR'
        ON CONFLICT DO NOTHING;
        """;

        await using var con = await _db.OpenAsync(ct);
        await con.ExecuteAsync(new CommandDefinition(sql, new
        {
            tenantId = DefaultTenantId,
            adminUserId,
            arquivistaUserId,
            administradorUserId,
            adminHash,
            arquivistaHash,
            administradorHash
        }, cancellationToken: ct));

        _logger.LogInformation("System Seed SUCCESS");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
