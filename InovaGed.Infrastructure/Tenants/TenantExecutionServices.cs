using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;

namespace InovaGed.Infrastructure.Tenants;

public sealed class DatabaseTenantCatalog : ITenantCatalog
{
    private static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly IDbConnectionFactory _db;
    public DatabaseTenantCatalog(IDbConnectionFactory db) => _db = db;
    public async Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await _db.OpenAsync(cancellationToken);
            var ids = await conn.QueryAsync<Guid>(new CommandDefinition("select id from ged.tenant where coalesce(reg_status,'A')='A'", cancellationToken: cancellationToken));
            var list = ids.Distinct().ToList();
            return list.Count == 0 ? [DefaultTenantId] : list;
        }
        catch { return [DefaultTenantId]; }
    }
}

public sealed record TenantExecutionContext(Guid TenantId, string CorrelationId) : ITenantExecutionContext;

public sealed class ConfiguredSystemUserProvider : ISystemUserProvider
{
    private static readonly Guid DefaultSystemUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid GetSystemUserId(Guid tenantId) => DefaultSystemUser;
    public string GetSystemUserName(Guid tenantId) => $"Sistema InovaGED ({tenantId})";
}
