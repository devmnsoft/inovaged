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
WITH tokens AS (
  SELECT
    regexp_replace(lower(t), '[^[:alnum:]À-ÿ_]+', '', 'g') AS tok
  FROM regexp_split_to_table(trim(@q), '\s+') AS t
),
query AS (
  SELECT
    to_tsquery('portuguese', string_agg(tok || ':*', ' & ')) AS tsq
  FROM tokens
  WHERE tok <> ''
)
SELECT
  d.id AS ""DocumentId"",
  s.version_id AS ""VersionId"",
  d.code AS ""Code"",
  d.title AS ""Title"",
  s.file_name AS ""FileName"",
  ts_headline(
    'portuguese',
    COALESCE(s.ocr_text, d.description, ''),
    (SELECT tsq FROM query),
    'StartSel=<mark>, StopSel=</mark>, MaxFragments=2, FragmentDelimiter= … , MaxWords=20, MinWords=8'
  ) AS ""Snippet"",
  ts_rank(s.search_vector, (SELECT tsq FROM query)) AS ""Rank""
FROM ged.document_search s
JOIN ged.document d
  ON d.tenant_id = s.tenant_id
 AND d.id = s.document_id
WHERE s.tenant_id = @tenantId
  AND (SELECT tsq FROM query) IS NOT NULL
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

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na busca full-text. Tenant={TenantId}, q={q}", tenantId, q);
            throw;
        }
    }
}
