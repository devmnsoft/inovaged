using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Search;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Search;

public sealed class GedSmartSearchService : IGedSmartSearchService
{
    private static readonly string[] ClinicalTerms =
    [
        "carcinoma", "câncer", "cancer", "biópsia", "biopsia", "quimioterapia", "apac", "laudo",
        "exame", "cirurgia", "internação", "internacao", "uti", "sepse"
    ];

    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GedSmartSearchService> _logger;

    public GedSmartSearchService(IDbConnectionFactory db, IMemoryCache cache, ILogger<GedSmartSearchService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SmartSearchSuggestionDto>> SuggestAsync(SmartSearchRequest request, CancellationToken ct)
    {
        var normalized = NormalizeQuery(request.Query);
        if (!CanSearch(normalized)) return Array.Empty<SmartSearchSuggestionDto>();

        request.Limit = Math.Clamp(request.Limit <= 0 ? 16 : request.Limit, 1, 20);
        var cacheKey = $"GedSmartSearch:Suggest:{request.TenantId}:{request.UserId}:{request.Module}:{request.Scope}:{request.FolderId}:{normalized.ToLowerInvariant()}:{request.Limit}:{request.IsAdmin}";
        if (_cache.TryGetValue<IReadOnlyList<SmartSearchSuggestionDto>>(cacheKey, out var cached)) return cached;

        var sw = Stopwatch.StartNew();
        try
        {
            var rows = await QueryRowsAsync(request, normalized, request.Limit, ct);
            var items = rows.Select(x => ToSuggestion(x, normalized))
                .Concat(BuildClinicalSuggestions(normalized!))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Title)
                .Take(request.Limit)
                .ToList();

            _cache.Set(cacheKey, items, TimeSpan.FromSeconds(30));
            _logger.LogInformation("Smart suggestions GED executado. Tenant={TenantId} User={UserId} Query={Query} FolderId={FolderId} Scope={Scope} Module={Module} ResultCount={ResultCount} ElapsedMs={ElapsedMs}",
                request.TenantId, request.UserId, normalized, request.FolderId, request.Scope, request.Module, items.Count, sw.ElapsedMilliseconds);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar sugestões inteligentes GED. Tenant={TenantId} User={UserId} Query={Query} FolderId={FolderId} Scope={Scope}", request.TenantId, request.UserId, normalized, request.FolderId, request.Scope);
            throw;
        }
    }

    public async Task<GedSearchResultDto> SearchAsync(SmartSearchRequest request, CancellationToken ct)
    {
        var normalized = NormalizeQuery(request.Query);
        request.Limit = Math.Clamp(request.Limit <= 0 ? 50 : request.Limit, 1, 100);
        var sw = Stopwatch.StartNew();
        var rows = await QueryRowsAsync(request, normalized, request.Limit, ct);
        var items = rows.Select(x => new GedSearchResultItemDto
        {
            DocumentId = x.DocumentId,
            VersionId = x.VersionId,
            Title = x.Title,
            OriginalFileName = x.FileName,
            FileExtension = Path.GetExtension(x.FileName ?? string.Empty),
            FolderName = x.FolderName,
            FolderPath = x.FolderPath,
            FolderId = x.FolderId,
            DocumentType = x.DocumentType,
            ClassificationCode = x.ClassificationCode,
            ClassificationName = x.ClassificationName,
            OcrStatus = x.OcrStatus,
            DocumentStatus = x.DocumentStatus,
            CreatedAt = x.CreatedAt,
            HasOcr = x.HasOcr,
            HasSuggestion = false,
            OcrSnippet = x.Snippet,
            Score = x.Score,
            CanView = true,
            CanDownload = true,
            CanClassify = request.IsAdmin,
            CanMove = request.IsAdmin
        }).ToList();

        _logger.LogInformation("Smart search GED executado. Tenant={TenantId} User={UserId} Query={Query} FolderId={FolderId} Scope={Scope} Module={Module} ResultCount={ResultCount} ElapsedMs={ElapsedMs}",
            request.TenantId, request.UserId, normalized, request.FolderId, request.Scope, request.Module, items.Count, sw.ElapsedMilliseconds);

        return new GedSearchResultDto { Items = items, Total = items.Count, Page = 1, PageSize = request.Limit, TotalPages = items.Count == 0 ? 0 : 1 };
    }

    private async Task<IReadOnlyList<SmartSearchRow>> QueryRowsAsync(SmartSearchRequest request, string? normalized, int limit, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId, DbType.Guid);
        p.Add("UserId", request.UserId, DbType.Guid);
        p.Add("FolderId", string.Equals(request.Scope, "global", StringComparison.OrdinalIgnoreCase) ? null : request.FolderId, DbType.Guid);
        p.Add("Q", normalized, DbType.String);
        p.Add("Limit", limit, DbType.Int32);

        const string sql = @"
WITH candidates AS (
    SELECT
        d.id AS document_id,
        COALESCE(NULLIF(ds.version_id, '00000000-0000-0000-0000-000000000000'::uuid), NULLIF(d.current_version_id, '00000000-0000-0000-0000-000000000000'::uuid), latest_v.id) AS version_id,
        d.folder_id,
        COALESCE(NULLIF(d.title, ''), 'Documento sem título') AS title,
        COALESCE(NULLIF(ds.file_name, ''), NULLIF(d.original_file_name, ''), NULLIF(latest_v.file_name, ''), 'arquivo') AS file_name,
        COALESCE(f.name, 'Sem pasta') AS folder_name,
        f.path AS folder_path,
        dt.name AS document_type,
        cp.code AS classification_code,
        cp.name AS classification_name,
        COALESCE(oj.status::text, ds.ocr_status::text, 'NONE') AS ocr_status,
        d.status::text AS document_status,
        d.created_at,
        COALESCE(d.code, '') AS code,
        COALESCE(d.description, '') AS description,
        substring(COALESCE(ds.ocr_text, '') from 1 for 4000) AS ocr_limited
    FROM ged.document d
    LEFT JOIN ged.document_search ds ON ds.tenant_id = d.tenant_id AND ds.document_id = d.id
    LEFT JOIN ged.folder f ON f.tenant_id = d.tenant_id AND f.id = d.folder_id
    LEFT JOIN ged.document_type dt ON dt.tenant_id = d.tenant_id AND dt.id = d.document_type_id
    LEFT JOIN ged.document_classification dc ON dc.tenant_id = d.tenant_id AND dc.document_id = d.id AND dc.reg_status = 'A'
    LEFT JOIN ged.classification_plan cp ON cp.tenant_id = d.tenant_id AND cp.id = dc.classification_id
    LEFT JOIN LATERAL (
        SELECT vx.id, vx.file_name
        FROM ged.document_version vx
        WHERE vx.tenant_id = d.tenant_id AND vx.document_id = d.id
        ORDER BY vx.version_number DESC, vx.created_at DESC
        LIMIT 1
    ) latest_v ON true
    LEFT JOIN LATERAL (
        SELECT j.status
        FROM ged.ocr_job j
        WHERE j.tenant_id = d.tenant_id
          AND j.document_version_id = COALESCE(NULLIF(ds.version_id, '00000000-0000-0000-0000-000000000000'::uuid), NULLIF(d.current_version_id, '00000000-0000-0000-0000-000000000000'::uuid), latest_v.id)
        ORDER BY j.requested_at DESC
        LIMIT 1
    ) oj ON true
    WHERE d.tenant_id = @TenantId::uuid
      AND COALESCE(d.reg_status, 'A') = 'A'
      AND UPPER(COALESCE(d.status::text, '')) <> 'DELETED'
      AND (@FolderId::uuid IS NULL OR d.folder_id = @FolderId::uuid)
), ranked AS (
    SELECT *,
        CASE
            WHEN @Q::text IS NULL THEN 0
            WHEN lower(title) LIKE lower(@Q::text) || '%' THEN 100 ELSE 0 END +
        CASE WHEN @Q::text IS NOT NULL AND title ILIKE '%' || @Q::text || '%' THEN 80 ELSE 0 END +
        CASE WHEN @Q::text IS NOT NULL AND file_name ILIKE '%' || @Q::text || '%' THEN 70 ELSE 0 END +
        CASE WHEN @Q::text IS NOT NULL AND code ILIKE @Q::text THEN 100 ELSE 0 END +
        CASE WHEN @Q::text IS NOT NULL AND COALESCE(folder_name, '') ILIKE '%' || @Q::text || '%' THEN 50 ELSE 0 END +
        CASE WHEN @Q::text IS NOT NULL AND COALESCE(document_type, '') ILIKE '%' || @Q::text || '%' THEN 45 ELSE 0 END +
        CASE WHEN @Q::text IS NOT NULL AND ocr_limited ILIKE '%' || @Q::text || '%' THEN 60 ELSE 0 END +
        CASE WHEN created_at >= now() - interval '30 days' THEN 5 ELSE 0 END +
        CASE WHEN @FolderId::uuid IS NOT NULL AND folder_id = @FolderId::uuid THEN 10 ELSE 0 END AS score
    FROM candidates
    WHERE version_id IS NOT NULL
      AND (
        @Q::text IS NULL
        OR title ILIKE '%' || @Q::text || '%'
        OR file_name ILIKE '%' || @Q::text || '%'
        OR code ILIKE '%' || @Q::text || '%'
        OR description ILIKE '%' || @Q::text || '%'
        OR COALESCE(folder_name, '') ILIKE '%' || @Q::text || '%'
        OR COALESCE(folder_path, '') ILIKE '%' || @Q::text || '%'
        OR COALESCE(document_type, '') ILIKE '%' || @Q::text || '%'
        OR ocr_limited ILIKE '%' || @Q::text || '%'
      )
)
SELECT document_id AS ""DocumentId"", version_id AS ""VersionId"", folder_id AS ""FolderId"", title AS ""Title"", file_name AS ""FileName"",
       folder_name AS ""FolderName"", folder_path AS ""FolderPath"", document_type AS ""DocumentType"",
       classification_code AS ""ClassificationCode"", classification_name AS ""ClassificationName"", ocr_status AS ""OcrStatus"",
       document_status AS ""DocumentStatus"", created_at AS ""CreatedAt"", (ocr_limited <> '') AS ""HasOcr"",
       CASE
           WHEN @Q::text IS NOT NULL AND ocr_limited ILIKE '%' || @Q::text || '%' THEN substring(ocr_limited from greatest(1, position(lower(@Q::text) in lower(ocr_limited)) - 70) for 180)
           WHEN @Q::text IS NOT NULL AND description ILIKE '%' || @Q::text || '%' THEN substring(description from 1 for 180)
           ELSE NULL
       END AS ""Snippet"",
       score::numeric AS ""Score""
FROM ranked
WHERE score > 0 OR @Q::text IS NULL
ORDER BY score DESC, created_at DESC
LIMIT @Limit::int;";

        var rows = await conn.QueryAsync<SmartSearchRow>(new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.AsList();
    }

    private static SmartSearchSuggestionDto ToSuggestion(SmartSearchRow x, string? query)
    {
        var hasOcrMatch = !string.IsNullOrWhiteSpace(x.Snippet);
        var group = hasOcrMatch ? "OCR / Conteúdo" : "Documentos";
        return new SmartSearchSuggestionDto
        {
            Group = group,
            Type = hasOcrMatch ? "ocr" : "document",
            DocumentId = x.DocumentId,
            VersionId = x.VersionId,
            FolderId = x.FolderId,
            Title = x.Title,
            Subtitle = hasOcrMatch ? $"Encontrado no OCR · Pasta: {x.FolderName}" : $"Pasta: {x.FolderPath ?? x.FolderName} · {x.DocumentType ?? "Tipo não informado"}",
            Snippet = TruncateSnippet(x.Snippet, 180),
            Icon = IconFor(x.FileName, hasOcrMatch),
            Url = $"/Ged/Details/{x.DocumentId}",
            Score = x.Score
        };
    }

    private static IEnumerable<SmartSearchSuggestionDto> BuildClinicalSuggestions(string query)
    {
        if (!ClinicalTerms.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase) || query.Contains(t, StringComparison.OrdinalIgnoreCase))) yield break;
        yield return new SmartSearchSuggestionDto
        {
            Group = "Termos clínicos encontrados",
            Type = "clinical-term",
            Title = query,
            Subtitle = "Pesquisar termo clínico em documentos e OCR permitido",
            Icon = "bi-heart-pulse",
            Url = $"/Ged/Search?q={Uri.EscapeDataString(query)}&scope=global",
            Score = 65
        };
    }

    private static string? NormalizeQuery(string? query) => string.IsNullOrWhiteSpace(query) ? null : Regex.Replace(query.Trim(), "\\s+", " ");
    private static bool CanSearch(string? query) => !string.IsNullOrWhiteSpace(query) && (query.Length >= 3 || (query.Length >= 2 && query.All(char.IsDigit)));
    private static string? TruncateSnippet(string? text, int max) => string.IsNullOrWhiteSpace(text) ? null : (text.Length <= max ? text : text[..max] + "…");
    private static string IconFor(string? fileName, bool ocr) => ocr ? "bi-body-text" : (Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch { ".pdf" => "bi-file-earmark-pdf", ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" => "bi-file-earmark-image", ".doc" or ".docx" => "bi-file-earmark-word", _ => "bi-file-earmark-text" });

    private sealed class SmartSearchRow
    {
        public Guid DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public Guid? FolderId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? FolderName { get; set; }
        public string? FolderPath { get; set; }
        public string? DocumentType { get; set; }
        public string? ClassificationCode { get; set; }
        public string? ClassificationName { get; set; }
        public string? OcrStatus { get; set; }
        public string? DocumentStatus { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public bool HasOcr { get; set; }
        public string? Snippet { get; set; }
        public decimal Score { get; set; }
    }
}
