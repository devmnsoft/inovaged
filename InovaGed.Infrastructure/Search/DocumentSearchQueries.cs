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
        string? q,
        Guid? folderId,
        int limit,
        CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        var normalizedQuery = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        const string sql = @"
WITH filtered AS (
  SELECT
    d.id,
    d.code,
    d.title,
    d.description,
    d.created_at,
    COALESCE(NULLIF(s.version_id, '00000000-0000-0000-0000-000000000000'::uuid), NULLIF(d.current_version_id, '00000000-0000-0000-0000-000000000000'::uuid), latest_v.id) AS version_id,
    COALESCE(NULLIF(s.file_name, ''), NULLIF(latest_v.file_name, ''), 'arquivo') AS file_name,
    substring(COALESCE(s.ocr_text, '') from 1 for 4000) AS ocr_snippet,
    s.search_vector
  FROM ged.document d
  LEFT JOIN ged.document_search s ON s.tenant_id = d.tenant_id AND s.document_id = d.id
  LEFT JOIN LATERAL (
    SELECT vx.id, vx.file_name
    FROM ged.document_version vx
    WHERE vx.tenant_id = d.tenant_id AND vx.document_id = d.id
    ORDER BY vx.version_number DESC, vx.created_at DESC
    LIMIT 1
  ) latest_v ON true
  WHERE d.tenant_id = @TenantId::uuid
    AND d.reg_status = 'A'::bpchar
    AND (@FolderId::uuid IS NULL OR d.folder_id = @FolderId::uuid)
), ranked AS (
  SELECT
    f.id AS ""DocumentId"",
    f.version_id AS ""VersionId"",
    COALESCE(NULLIF(f.code, ''), f.id::text) AS ""Code"",
    COALESCE(NULLIF(f.title, ''), 'Documento sem título') AS ""Title"",
    f.file_name AS ""FileName"",
    CASE
      WHEN @Q::text IS NULL THEN 'Documento encontrado.'
      WHEN f.title ILIKE '%' || @Q::text || '%' THEN regexp_replace(f.title, @Q::text, '<mark>' || @Q::text || '</mark>', 'ig')
      WHEN f.file_name ILIKE '%' || @Q::text || '%' THEN regexp_replace(f.file_name, @Q::text, '<mark>' || @Q::text || '</mark>', 'ig')
      WHEN COALESCE(f.description, '') ILIKE '%' || @Q::text || '%' THEN substring(COALESCE(f.description, '') from 1 for 300)
      WHEN f.ocr_snippet ILIKE '%' || @Q::text || '%' THEN substring(f.ocr_snippet from 1 for 300)
      ELSE 'Documento encontrado pelos filtros informados.'
    END AS ""Snippet"",
    CASE
      WHEN @Q::text IS NULL THEN 0::real
      WHEN COALESCE(f.code, '') ILIKE @Q::text THEN 100::real
      WHEN f.title ILIKE '%' || @Q::text || '%' THEN 80::real
      WHEN f.file_name ILIKE '%' || @Q::text || '%' THEN 70::real
      WHEN COALESCE(f.description, '') ILIKE '%' || @Q::text || '%' THEN 50::real
      WHEN f.ocr_snippet ILIKE '%' || @Q::text || '%' THEN 40::real
      ELSE 1::real
    END AS ""Rank"",
    f.created_at
  FROM filtered f
  WHERE f.version_id IS NOT NULL
    AND (
      @Q::text IS NULL
      OR COALESCE(f.code, '') ILIKE @Q::text
      OR f.title ILIKE '%' || @Q::text || '%'
      OR f.file_name ILIKE '%' || @Q::text || '%'
      OR COALESCE(f.description, '') ILIKE '%' || @Q::text || '%'
      OR f.ocr_snippet ILIKE '%' || @Q::text || '%'
    )
)
SELECT ""DocumentId"", ""VersionId"", ""Code"", ""Title"", ""FileName"", ""Snippet"", ""Rank""
FROM ranked
ORDER BY ""Rank"" DESC, created_at DESC
LIMIT @Limit;
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<DocumentSearchRowDto>(
                new CommandDefinition(sql, new { TenantId = tenantId, Q = normalizedQuery, FolderId = folderId, Limit = limit }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na busca GED. Tenant={TenantId} FolderId={FolderId} q={Query}", tenantId, folderId, normalizedQuery);
            throw;
        }
    }
}
