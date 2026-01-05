using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Classification;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class ClassificationDashboardQueries : IClassificationDashboardQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ClassificationDashboardQueries> _logger;

    public ClassificationDashboardQueries(IDbConnectionFactory db, ILogger<ClassificationDashboardQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> CountAsync(Guid tenantId, Guid? folderId, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(1)
FROM ged.document d
WHERE d.tenant_id = @tenantId
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND d.type_id IS NULL
  AND (
        (@folderId IS NULL AND d.folder_id IS NULL)
        OR
        (@folderId IS NOT NULL AND d.folder_id = @folderId)
  );";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassificationDashboardQueries.CountAsync ERROR | Tenant={Tenant} Folder={Folder}", tenantId, folderId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ClassificationFolderCountDto>> ByFolderAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  d.folder_id AS ""FolderId"",
  COALESCE(f.name,'(Sem pasta)') AS ""FolderName"",
  COUNT(1) AS ""Count""
FROM ged.document d
LEFT JOIN ged.folder f
       ON f.tenant_id = d.tenant_id
      AND f.id = d.folder_id
WHERE d.tenant_id = @tenantId
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND d.type_id IS NULL
GROUP BY d.folder_id, f.name
ORDER BY COUNT(1) DESC, COALESCE(f.name,'') ASC;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<ClassificationFolderCountDto>(
                new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassificationDashboardQueries.ByFolderAsync ERROR | Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<IReadOnlyList<UnclassifiedRowDto>> ListAsync(
        Guid tenantId, Guid? folderId, int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var offset = (page - 1) * pageSize;

        const string sql = @"
SELECT
  d.id AS ""Id"",
  d.title AS ""Title"",
  v.file_name AS ""FileName"",
  d.folder_id AS ""FolderId"",
  COALESCE(f.name,'(Sem pasta)') AS ""FolderName"",
  d.created_at AS ""CreatedAt""
FROM ged.document d
LEFT JOIN ged.document_version v
       ON v.tenant_id = d.tenant_id
      AND v.id = d.current_version_id
LEFT JOIN ged.folder f
       ON f.tenant_id = d.tenant_id
      AND f.id = d.folder_id
WHERE d.tenant_id = @tenantId
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND d.type_id IS NULL
  AND (
        (@folderId IS NULL AND d.folder_id IS NULL)
        OR
        (@folderId IS NOT NULL AND d.folder_id = @folderId)
  )
ORDER BY d.created_at DESC
OFFSET @offset LIMIT @pageSize;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var list = await conn.QueryAsync<UnclassifiedRowDto>(
                new CommandDefinition(sql, new { tenantId, folderId, offset, pageSize }, cancellationToken: ct));

            return list.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ClassificationDashboardQueries.ListAsync ERROR | Tenant={Tenant} Folder={Folder} Page={Page} Size={Size}",
                tenantId, folderId, page, pageSize);
            throw;
        }
    }
}
