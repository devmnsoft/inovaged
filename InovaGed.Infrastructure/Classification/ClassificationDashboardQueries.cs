using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Classification;

namespace InovaGed.Infrastructure.Classification;

public sealed class ClassificationDashboardQueries : IClassificationDashboardQueries
{
    private readonly IDbConnectionFactory _db;
    public ClassificationDashboardQueries(IDbConnectionFactory db) => _db = db;

    public async Task<int> CountAsync(Guid tenantId, Guid? folderId, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(1)
FROM ged.documents d
WHERE d.tenant_id = @tenantId
  AND d.reg_status = 'A'
  AND (@folderId IS NULL OR d.folder_id = @folderId)
  AND (d.document_type_id IS NULL);
";
        using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<(Guid? FolderId, string FolderName, int Count)>> ByFolderAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  d.folder_id AS ""FolderId"",
  COALESCE(f.name,'(Sem pasta)') AS ""FolderName"",
  COUNT(1) AS ""Count""
FROM ged.documents d
LEFT JOIN ged.folders f ON f.tenant_id = d.tenant_id AND f.id = d.folder_id
WHERE d.tenant_id = @tenantId
  AND d.reg_status = 'A'
  AND d.document_type_id IS NULL
GROUP BY d.folder_id, f.name
ORDER BY COUNT(1) DESC, COALESCE(f.name,'') ASC;
";
        using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync(sql, new { tenantId });
        return rows.Select(r => ((Guid?)r.FolderId, (string)r.FolderName, (int)r.Count)).ToList();
    }

    public async Task<IReadOnlyList<UnclassifiedRowDto>> ListAsync(Guid tenantId, Guid? folderId, int page, int pageSize, CancellationToken ct)
    {
        var offset = (page <= 1 ? 0 : (page - 1) * pageSize);

        const string sql = @"
SELECT
  d.id AS ""Id"",
  d.title AS ""Title"",
  cv.file_name AS ""FileName"",
  d.folder_id AS ""FolderId"",
  COALESCE(f.name,'(Sem pasta)') AS ""FolderName"",
  d.created_at AS ""CreatedAt""
FROM ged.documents d
JOIN ged.document_current_version cv ON cv.tenant_id = d.tenant_id AND cv.document_id = d.id
LEFT JOIN ged.folders f ON f.tenant_id = d.tenant_id AND f.id = d.folder_id
WHERE d.tenant_id = @tenantId
  AND d.reg_status = 'A'
  AND (@folderId IS NULL OR d.folder_id = @folderId)
  AND d.document_type_id IS NULL
ORDER BY d.created_at DESC
OFFSET @offset LIMIT @pageSize;
";
        using var conn = await _db.OpenAsync(ct);
        var list = await conn.QueryAsync<UnclassifiedRowDto>(
            new CommandDefinition(sql, new { tenantId, folderId, offset, pageSize }, cancellationToken: ct));
        return list.ToList();
    }
}
