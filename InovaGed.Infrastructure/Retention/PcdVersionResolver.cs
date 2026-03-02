using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Retention;

public sealed class PcdVersionResolver : IPcdVersionResolver
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<PcdVersionResolver> _logger;

    public PcdVersionResolver(IDbConnectionFactory db, ILogger<PcdVersionResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Guid?> GetLatestPublishedVersionIdAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
select id
from ged.classification_plan_version
where tenant_id=@tenantId and reg_status='A'
order by version_no desc
limit 1;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<Guid?>(sql, new { tenantId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetLatestPublishedVersionIdAsync failed. Tenant={TenantId}", tenantId);
            return null;
        }
    }
}