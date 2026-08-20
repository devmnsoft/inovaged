using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;
using Npgsql;

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
        if (tenantId == Guid.Empty)
            return null;

        await using var conn = await _db.OpenAsync(ct);
        var hasTable = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select to_regclass('ged.classification_plan_version') is not null",
            cancellationToken: ct));

        if (!hasTable)
        {
            _logger.LogWarning(
                "Tabela ged.classification_plan_version não existe. PCD version será gravada como null. Tenant={TenantId}",
                tenantId);
            return null;
        }

        const string sql = """
select id
from ged.classification_plan_version
where tenant_id = @tenantId
  and coalesce(reg_status, 'A') = 'A'
  and (
      published_at is not null
      or upper(coalesce(status, '')) in ('PUBLISHED', 'PUBLICADO', 'ATIVO', 'ACTIVE')
  )
order by
  published_at desc nulls last,
  created_at desc nulls last,
  version_no desc nulls last
limit 1
""";

        try
        {
            return await conn.QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            _logger.LogWarning(
                ex,
                "Schema parcial em ged.classification_plan_version. Tentando fallback simples. Tenant={TenantId}",
                tenantId);

            const string fallbackSql = """
select id
from ged.classification_plan_version
where tenant_id = @tenantId
  and coalesce(reg_status, 'A') = 'A'
order by created_at desc nulls last
limit 1
""";

            return await conn.QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(fallbackSql, new { tenantId }, cancellationToken: ct));
        }
    }
}
