using System.Data;
using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Setup;

public sealed class SystemSeedHostedService : IHostedService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SystemSeedHostedService> _logger;

    public SystemSeedHostedService(IDbConnectionFactory db, ILogger<SystemSeedHostedService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("System Seed START");

        const string sql = """
        -- 0) uuid helper
        CREATE EXTENSION IF NOT EXISTS pgcrypto;

        -- 1) garante ROLE ADMIN (chave lógica = tenant_id + normalized_name)
        INSERT INTO ged.app_role (id, tenant_id, name, normalized_name, created_at)
        VALUES (
          gen_random_uuid(),
          '00000000-0000-0000-0000-000000000001',
          'ADMIN',
          'ADMIN',
          now()
        )
        ON CONFLICT (tenant_id, normalized_name) DO UPDATE
        SET name = EXCLUDED.name;

        -- 2) garante USER Admin (ajuste se seu schema pedir mais colunas NOT NULL)
        INSERT INTO ged.app_user (id, tenant_id, name, email, password_hash, is_active, created_at)
        VALUES (
          'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001',
          '00000000-0000-0000-0000-000000000001',
          'Admin',
          'admin@local',
          'DEV-SEED-NOT-A-REAL-HASH',
          true,
          now()
        )
        ON CONFLICT (id) DO UPDATE
        SET
          name = EXCLUDED.name,
          email = EXCLUDED.email,
          is_active = EXCLUDED.is_active;

        -- 3) vincula user -> role ADMIN (pega o role_id real)
        INSERT INTO ged.user_role (user_id, role_id)
        SELECT
          'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001'::uuid,
          r.id
        FROM ged.app_role r
        WHERE r.tenant_id = '00000000-0000-0000-0000-000000000001'
          AND r.normalized_name = 'ADMIN'
        ON CONFLICT DO NOTHING;
        """;

        try
        {
            await using var con = await _db.OpenAsync(ct);
            await con.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
            _logger.LogInformation("System Seed SUCCESS");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System Seed ERROR");
            throw;
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}