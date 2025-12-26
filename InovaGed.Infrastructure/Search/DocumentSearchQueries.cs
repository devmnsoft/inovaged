using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Search;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Search;

public sealed class DocumentSearchQueries : IDocumentSearchQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentSearchQueries> _logger;

    public DocumentSearchQueries(IDbConnectionFactory db, ILogger<DocumentSearchQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentSearchRowDto>> SearchAsync(
        Guid tenantId,
        string q,
        Guid? folderId,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Array.Empty<DocumentSearchRowDto>();

        limit = Math.Clamp(limit, 1, 100);

        const string sql = @"
WITH query AS (
  SELECT plainto_tsquery('portuguese', @q) AS tsq
)
SELECT
  d.id AS ""DocumentId"",
  s.version_id AS ""VersionId"",
  d.code AS ""Code"",
  d.title AS ""Title"",
  s.file_name AS ""FileName"",
  ts_headline('portuguese',
    COALESCE(s.ocr_text, d.description, ''),
    (SELECT tsq FROM query),
    'StartSel=<mark>, StopSel=</mark>, MaxFragments=2, FragmentDelimiter= … , MaxWords=20, MinWords=8'
  ) AS ""Snippet"",
  ts_rank(s.search_vector, (SELECT tsq FROM query)) AS ""Rank""
FROM ged.document_search s
JOIN ged.document d
  ON d.tenant_id = s.tenant_id
 AND d.id = s.document_id
JOIN query ON true
WHERE s.tenant_id = @tenantId
  AND s.search_vector @@ (SELECT tsq FROM query)
  AND (@folderId IS NULL OR d.folder_id = @folderId)
ORDER BY ""Rank"" DESC, d.created_at DESC
LIMIT @limit;
";

        try
        {
            var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<DocumentSearchRowDto>(
                new CommandDefinition(sql, new { tenantId, q, folderId, limit }, cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na busca full-text. Tenant={TenantId}, q={q}", tenantId, q);
            throw;
        }
    }
}
