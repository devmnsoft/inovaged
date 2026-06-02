using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged;

public sealed class FolderQueries : IFolderQueries
{
    public static string TreeCacheKey(Guid tenantId) => $"GedFolderTree:{tenantId}:TENANT";

    private static readonly TimeSpan FolderTreeCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FolderQueries> _logger;

    public FolderQueries(IDbConnectionFactory db, IMemoryCache cache, ILogger<FolderQueries> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FolderNodeDto>> TreeAsync(Guid tenantId, CancellationToken ct)
    {
        var cacheKey = TreeCacheKey(tenantId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<FolderNodeDto>? cached) && cached is not null)
        {
            _logger.LogDebug("Árvore de pastas obtida do cache. Tenant={TenantId}", tenantId);
            return cached;
        }

        try
        {
            _logger.LogInformation("Carregando árvore de pastas. Tenant={Tenant}", tenantId);

            const string sql = @"
WITH RECURSIVE t AS (
    SELECT 
        f.id,
        f.parent_id AS ""ParentId"",
        f.name      AS ""Name"",
        0           AS ""Level""
    FROM ged.folder f
    WHERE f.tenant_id = @tenantId
      AND f.parent_id IS NULL
      AND f.is_active = TRUE
      AND f.reg_status = 'A'

    UNION ALL

    SELECT
        c.id,
        c.parent_id AS ""ParentId"",
        c.name      AS ""Name"",
        t.""Level"" + 1 AS ""Level""
    FROM ged.folder c
    JOIN t ON t.id = c.parent_id
    WHERE c.tenant_id = @tenantId
      AND c.is_active = TRUE
      AND c.reg_status = 'A'
)
SELECT * FROM t
ORDER BY ""Level"", ""Name"";";

            using var conn = await _db.OpenAsync(ct);

            var rows = (await conn.QueryAsync<FolderNodeDto>(
                new CommandDefinition(sql, new { tenantId }, cancellationToken: ct))).AsList();

            _cache.Set(cacheKey, rows, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = FolderTreeCacheTtl,
                Size = rows.Count == 0 ? 1 : rows.Count
            });

            return rows;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Carregamento da árvore de pastas foi cancelado. Tenant={TenantId}", tenantId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar árvore de pastas. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(Guid tenantId, Guid folderId, CancellationToken ct)
    {
        const string sql = @"
select exists(
    select 1
    from ged.folder
    where tenant_id = @tenantId
      and id = @folderId
      and is_active = true
      and coalesce(reg_status, 'A') = 'A'
);";

        using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));
    }
}
