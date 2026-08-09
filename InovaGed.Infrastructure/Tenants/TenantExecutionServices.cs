using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Configuration;

namespace InovaGed.Infrastructure.Tenants;

public sealed class DatabaseTenantCatalog : ITenantCatalog
{
    private readonly IDbConnectionFactory _db;
    public DatabaseTenantCatalog(IDbConnectionFactory db) => _db = db;
    public async Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await _db.OpenAsync(cancellationToken);
            var ids = await conn.QueryAsync<Guid>(new CommandDefinition("select id from ged.tenant where coalesce(reg_status,'A')='A'", cancellationToken: cancellationToken));
            var list = ids.Distinct().ToList();
            return list;
        }
        catch { return Array.Empty<Guid>(); }
    }
}

public sealed record TenantExecutionContext(Guid TenantId, string CorrelationId) : ITenantExecutionContext;

public sealed class ConfiguredSystemUserProvider : ISystemUserProvider
{
    private readonly IConfiguration _configuration;
    public ConfiguredSystemUserProvider(IConfiguration configuration) => _configuration = configuration;

    public Guid GetSystemUserId(Guid tenantId)
    {
        var configured = _configuration[$"SystemUsers:Tenants:{tenantId}:UserId"];
        if (!Guid.TryParse(configured, out var userId) || userId == Guid.Empty)
            throw new InvalidOperationException($"Usuário de sistema não configurado para o tenant {tenantId}.");
        return userId;
    }
    public string GetSystemUserName(Guid tenantId) => $"Sistema InovaGED ({tenantId})";
}
