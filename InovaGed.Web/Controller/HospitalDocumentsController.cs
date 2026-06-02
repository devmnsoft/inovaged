using System.Text;
using System.Data;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Identity;
using InovaGed.Application.Audit;
using InovaGed.Web.Models.HospitalDocuments;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using System.Text.Json;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.HospitalDocumentsOrLoansAccess)]
public sealed class HospitalDocumentsController : Controller
{
    private static readonly Guid EmptyGuid = Guid.Empty;
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IPreviewGenerator _preview;
    private readonly IAuditWriter _audit;
    private readonly ILogger<HospitalDocumentsController> _logger;
    private readonly IMemoryCache _cache;

    public HospitalDocumentsController(IDbConnectionFactory db, ICurrentUser currentUser, IFileStorage storage, IPreviewGenerator preview, IAuditWriter audit, ILogger<HospitalDocumentsController> logger, IMemoryCache cache)
    { _db = db; _currentUser = currentUser; _storage = storage; _preview = preview; _audit = audit; _logger = logger; _cache = cache; }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await RegisterHospitalAccessAuditAsync("index", ct);
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q, string? type, string? ocrStatus, DateTime? dateFrom, DateTime? dateTo, string? folder, bool ocrRequired = false, bool recentOnly = false, bool previewOnly = false, string? sort = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Json(new HospitalDocumentSearchResultDto { Success = true, Query = q?.Trim() ?? string.Empty, Page = page, PageSize = pageSize });

        var sw = Stopwatch.StartNew();
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var correlationId = HttpContext.TraceIdentifier;
        var query = q.Trim();
        var likeQuery = $"%{query}%";
        var normalizedType = NormalizeType(type);
        var normalizedOcrStatus = NormalizeOcrStatus(ocrStatus);
        var normalizedSort = NormalizeSort(sort);
        var normalizedFolder = string.IsNullOrWhiteSpace(folder) ? null : $"%{folder.Trim()}%";
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

const string sql = """
WITH base AS (
SELECT d.id AS "DocumentId",
COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) AS "VersionId",
COALESCE(NULLIF(d.code,''),d.id::text) AS "Code", COALESCE(NULLIF(d.title,''),'Documento sem título') AS "Title",
COALESCE(NULLIF(s.file_name,''),NULLIF(v.file_name,''),NULLIF(latest_v.file_name,''),'arquivo') AS "FileName",
COALESCE(NULLIF(v.content_type,''),NULLIF(latest_v.content_type,''),'') AS "ContentType",
COALESCE(v.file_size_bytes, latest_v.file_size_bytes,0) AS "SizeBytes", d.created_at AS "CreatedAt", COALESCE(s.ocr_text,'') AS "OcrText",
f.name AS "FolderName", NULL::text AS "FolderPath", COALESCE(oj.status::text,'NONE') AS "OcrStatus",
CASE WHEN d.code ILIKE @qExact THEN 'CODE' WHEN d.title ILIKE @likeQuery THEN 'TITLE' WHEN COALESCE(s.file_name,'') ILIKE @likeQuery THEN 'FILE_NAME' WHEN COALESCE(d.description,'') ILIKE @likeQuery THEN 'DESCRIPTION' WHEN COALESCE(s.ocr_text,'') ILIKE @likeQuery THEN 'OCR' ELSE 'SEARCH_VECTOR' END AS "MatchSource",
CASE WHEN s.search_vector IS NOT NULL AND s.search_vector @@ websearch_to_tsquery('portuguese', @q) THEN ts_headline('portuguese',COALESCE(s.ocr_text,d.description,d.title,s.file_name,''),websearch_to_tsquery('portuguese', @q),'StartSel=<mark>, StopSel=</mark>, MaxFragments=2, FragmentDelimiter= … , MaxWords=16, MinWords=6') ELSE COALESCE(NULLIF(d.description,''),NULLIF(s.file_name,''),NULLIF(d.title,''),'Documento encontrado pelos metadados.') END AS "Snippet",
CASE WHEN d.code ILIKE @qExact THEN 100 WHEN d.title ILIKE @likeQuery THEN 85 WHEN COALESCE(s.file_name,'') ILIKE @likeQuery THEN 70 WHEN COALESCE(d.description,'') ILIKE @likeQuery THEN 62 WHEN COALESCE(s.ocr_text,'') ILIKE @likeQuery THEN 74 ELSE 50 END +
CASE WHEN s.search_vector IS NOT NULL AND s.search_vector @@ websearch_to_tsquery('portuguese', @q) THEN (ts_rank(s.search_vector,websearch_to_tsquery('portuguese', @q))*100)::int ELSE 0 END AS "MatchScore",
CASE WHEN s.search_vector IS NOT NULL AND s.search_vector @@ websearch_to_tsquery('portuguese', @q) THEN ts_rank(s.search_vector,websearch_to_tsquery('portuguese', @q)) ELSE 0 END AS "Rank"
FROM ged.document d
LEFT JOIN ged.document_search s ON s.tenant_id=d.tenant_id AND s.document_id=d.id
LEFT JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.id=s.version_id
LEFT JOIN ged.folder f ON f.tenant_id=d.tenant_id AND f.id=d.folder_id
LEFT JOIN LATERAL (SELECT vx.* FROM ged.document_version vx WHERE vx.tenant_id=d.tenant_id AND vx.document_id=d.id ORDER BY vx.version_number DESC, vx.created_at DESC LIMIT 1) latest_v ON true
LEFT JOIN LATERAL (SELECT j.status FROM ged.ocr_job j WHERE j.tenant_id=d.tenant_id AND j.document_version_id=COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) ORDER BY j.requested_at DESC LIMIT 1) oj ON true
WHERE d.tenant_id=@tenantId AND d.reg_status='A'::bpchar AND d.status<>'ARCHIVED'::ged.document_status_enum
AND (d.code ILIKE @likeQuery OR d.title ILIKE @likeQuery OR COALESCE(d.description,'') ILIKE @likeQuery OR COALESCE(s.file_name,'') ILIKE @likeQuery OR COALESCE(s.ocr_text,'') ILIKE @likeQuery OR (s.search_vector IS NOT NULL AND s.search_vector @@ websearch_to_tsquery('portuguese', @q)))
AND (@docType IS NULL OR (@docType='pdf' AND (lower(COALESCE(v.content_type,latest_v.content_type,'')) LIKE '%pdf%' OR lower(COALESCE(s.file_name,v.file_name,latest_v.file_name,'')) LIKE '%.pdf')) OR (@docType='word' AND (lower(COALESCE(v.content_type,latest_v.content_type,'')) LIKE '%word%' OR lower(COALESCE(s.file_name,v.file_name,latest_v.file_name,'')) LIKE '%.doc%' )) OR (@docType='image' AND (lower(COALESCE(v.content_type,latest_v.content_type,'')) LIKE 'image/%' OR lower(COALESCE(s.file_name,v.file_name,latest_v.file_name,'')) SIMILAR TO '%.(jpg|jpeg|png|tif|tiff|webp|gif)')))
AND (@ocrFilter IS NULL OR (@ocrFilter='with' AND NULLIF(COALESCE(s.ocr_text,''),'') IS NOT NULL) OR (@ocrFilter='without' AND NULLIF(COALESCE(s.ocr_text,''),'') IS NULL) OR upper(COALESCE(oj.status::text,'NONE'))=@ocrFilter)
AND (@ocrRequired = FALSE OR NULLIF(COALESCE(s.ocr_text,''),'') IS NOT NULL)
AND (CAST(@dateFrom AS timestamptz) IS NULL OR d.created_at >= CAST(@dateFrom AS timestamptz))
AND (CAST(@dateTo AS timestamptz) IS NULL OR d.created_at < (CAST(@dateTo AS timestamptz) + interval '1 day'))
AND (CAST(@recentOnly AS boolean) = FALSE OR d.created_at >= now() - interval '7 days')
AND (@folder IS NULL OR f.name ILIKE @folder)
AND (@previewOnly = FALSE OR lower(COALESCE(v.content_type,latest_v.content_type,'')) LIKE '%pdf%' OR lower(COALESCE(v.content_type,latest_v.content_type,'')) LIKE 'image/%' OR lower(COALESCE(s.file_name,v.file_name,latest_v.file_name,'')) SIMILAR TO '%.(pdf|jpg|jpeg|png|tif|tiff|webp|gif)')
), filtered AS (
SELECT * FROM base WHERE "VersionId" IS NOT NULL AND "VersionId" <> '00000000-0000-0000-0000-000000000000'::uuid
)
SELECT filtered.*, agg."TotalRows", agg."TotalWithOcr", agg."TotalWithoutOcr", COALESCE(type_counts."TotalByType", '{}') AS "TotalByType"
FROM filtered
CROSS JOIN LATERAL (SELECT count(*)::int AS "TotalRows", count(*) FILTER (WHERE NULLIF("OcrText", '') IS NOT NULL)::int AS "TotalWithOcr", count(*) FILTER (WHERE NULLIF("OcrText", '') IS NULL)::int AS "TotalWithoutOcr" FROM filtered) agg
CROSS JOIN LATERAL (SELECT COALESCE(jsonb_object_agg("TypeName", "TypeTotal"), '{}'::jsonb)::text AS "TotalByType" FROM (SELECT CASE WHEN lower("ContentType") LIKE '%pdf%' OR lower("FileName") LIKE '%.pdf' THEN 'PDF' WHEN lower("ContentType") LIKE 'image/%' OR lower("FileName") SIMILAR TO '%.(jpg|jpeg|png|tif|tiff|webp|gif)' THEN 'Imagem' WHEN lower("FileName") LIKE '%.doc%' THEN 'Documento Word' ELSE 'Documento' END AS "TypeName", count(*)::int AS "TypeTotal" FROM filtered GROUP BY 1) t) type_counts
ORDER BY CASE WHEN @sort='az' THEN lower("Title") END ASC,
CASE WHEN @sort='type' THEN lower("ContentType") END ASC,
CASE WHEN @sort='recent' THEN "CreatedAt" END DESC,
CASE WHEN @sort='relevance' THEN "MatchScore" END DESC,
"Rank" DESC, "CreatedAt" DESC
LIMIT @pageSize OFFSET @offset;
""";
        try {
            await using var conn = await _db.OpenAsync(ct);
            var offset = (page - 1) * pageSize;
            var parameters = new DynamicParameters();
            parameters.Add("tenantId", tenantId, DbType.Guid);
            parameters.Add("q", query, DbType.String);
            parameters.Add("likeQuery", likeQuery, DbType.String);
            parameters.Add("qExact", query, DbType.String);
            parameters.Add("docType", normalizedType, DbType.String);
            parameters.Add("ocrFilter", normalizedOcrStatus, DbType.String);
            parameters.Add("ocrRequired", ocrRequired, DbType.Boolean);
            parameters.Add("dateFrom", dateFrom?.Date, DbType.DateTime);
            parameters.Add("dateTo", dateTo?.Date, DbType.DateTime);
            parameters.Add("recentOnly", recentOnly, DbType.Boolean);
            parameters.Add("folder", normalizedFolder, DbType.String);
            parameters.Add("previewOnly", previewOnly, DbType.Boolean);
            parameters.Add("sort", normalizedSort, DbType.String);
            parameters.Add("offset", offset, DbType.Int32);
            parameters.Add("pageSize", pageSize, DbType.Int32);
            var rows = (await conn.QueryAsync<HospitalDocumentSearchRow>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
            var first = rows.FirstOrDefault();
            var total = first?.TotalRows ?? 0;
            var items = rows.Where(x => x.VersionId != EmptyGuid).Select(MapResult).ToList();
            var typeTotals = DeserializeTypeTotals(first?.TotalByType);
            var elapsedMs = sw.ElapsedMilliseconds;
            var result = new HospitalDocumentSearchResultDto { Success = true, Items = items, TotalResults = total, ReturnedCount = items.Count, TotalWithOcr = first?.TotalWithOcr ?? 0, TotalWithoutOcr = first?.TotalWithoutOcr ?? 0, TotalByType = typeTotals, ElapsedMs = elapsedMs, Query = query, Page = page, PageSize = pageSize, TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize), HasMore = offset + rows.Count < total };
            _logger.LogInformation("Hospital document search executed. TenantId={TenantId} UserId={UserId} Query={Query} Type={Type} OcrStatus={OcrStatus} Filters={Filters} TotalResults={TotalResults} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}", tenantId, userId, query, normalizedType, normalizedOcrStatus, new { dateFrom, dateTo, folder, ocrRequired, recentOnly, previewOnly, sort = normalizedSort }, total, elapsedMs, correlationId);
            await _audit.WriteAsync(tenantId, userId, "VIEW", "HOSPITAL_DOCUMENT_SEARCH", null, "Busca hospitalar executada", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { EventType = "INFO", tenantId, userId, query, type = normalizedType, filters = new { ocrStatus = normalizedOcrStatus, dateFrom, dateTo, folder, ocrRequired, recentOnly, previewOnly, sort = normalizedSort }, totalResults = total, elapsedMs, correlationId }, ct);
            return Json(result);
        } catch (Exception ex) { var errorCorrelationId = HttpContext.TraceIdentifier; _logger.LogError(ex, "Erro na busca hospitalar. CorrelationId={CorrelationId}", errorCorrelationId); return StatusCode(500, new { success = false, message = "Não foi possível executar a busca agora.", correlationId = errorCorrelationId }); }
    }


    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false });
        var tenantId = _currentUser.TenantId;
        var cacheKey = $"HospitalDocuments:Summary:{tenantId}";
        if (_cache.TryGetValue<HospitalDocumentSummaryDto>(cacheKey, out var cached)) return Json(cached);

        const string sql = """
SELECT count(DISTINCT d.id)::int AS "TotalDocuments",
count(DISTINCT d.id) FILTER (WHERE NULLIF(COALESCE(s.ocr_text,''),'') IS NOT NULL)::int AS "TotalWithOcr",
count(DISTINCT d.id) FILTER (WHERE NULLIF(COALESCE(s.ocr_text,''),'') IS NULL)::int AS "TotalWithoutOcr",
count(DISTINCT CASE WHEN lower(COALESCE(v.content_type, latest_v.content_type, '')) LIKE '%pdf%' OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.pdf' THEN 'PDF' WHEN lower(COALESCE(v.content_type, latest_v.content_type, '')) LIKE 'image/%' OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) SIMILAR TO '%.(jpg|jpeg|png|tif|tiff|webp|gif)' THEN 'Imagem' WHEN lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.doc%' THEN 'Documento Word' ELSE 'Documento' END)::int AS "TotalTypes",
max(d.created_at) AS "LatestIndexedAt"
FROM ged.document d
LEFT JOIN ged.document_search s ON s.tenant_id=d.tenant_id AND s.document_id=d.id
LEFT JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.id=s.version_id
LEFT JOIN LATERAL (SELECT vx.* FROM ged.document_version vx WHERE vx.tenant_id=d.tenant_id AND vx.document_id=d.id ORDER BY vx.version_number DESC, vx.created_at DESC LIMIT 1) latest_v ON true
WHERE d.tenant_id=@tenantId AND d.reg_status='A'::bpchar AND d.status<>'ARCHIVED'::ged.document_status_enum;
""";
        await using var conn = await _db.OpenAsync(ct);
        var summary = await conn.QuerySingleAsync<HospitalDocumentSummaryDto>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        summary.Success = true;
        _cache.Set(cacheKey, summary, TimeSpan.FromSeconds(60));
        return Json(summary);
    }

    [HttpGet]
    public async Task<IActionResult> Suggestions(string? q, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, items = Array.Empty<object>() });
        var sw = Stopwatch.StartNew();
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var query = string.IsNullOrWhiteSpace(q) ? string.Empty : q.Trim();
        if (query.Length < 3) return Json(new { success = true, items = Array.Empty<object>() });

        var cacheKey = $"HospitalDocuments:Suggestions:{tenantId}:{query.ToLowerInvariant()}";
        if (_cache.TryGetValue<IReadOnlyList<HospitalDocumentSuggestionDto>>(cacheKey, out var cachedItems))
        {
            _logger.LogInformation("Hospital suggestions cache hit. Tenant={TenantId} User={UserId} Query={Query} ResultCount={ResultCount} ElapsedMs={ElapsedMs} CacheHit={CacheHit}", tenantId, userId, query, cachedItems.Count, sw.ElapsedMilliseconds, true);
            return Json(new { success = true, items = cachedItems });
        }

        var like = $"%{query}%";
        const string sql = """
SELECT d.id AS "DocumentId", COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) AS "VersionId",
COALESCE(NULLIF(d.code,''),d.id::text) AS "Code", COALESCE(NULLIF(d.title,''),'Documento sem título') AS "Title", COALESCE(NULLIF(s.file_name,''),NULLIF(v.file_name,''),NULLIF(latest_v.file_name,''),'arquivo') AS "FileName",
COALESCE(NULLIF(v.content_type,''),NULLIF(latest_v.content_type,''),'') AS "ContentType", COALESCE(v.file_size_bytes,latest_v.file_size_bytes,0) AS "SizeBytes", d.created_at AS "CreatedAt", COALESCE(f.name,'Sem pasta') AS "FolderName", NULL::text AS "FolderPath",
CASE WHEN NULLIF(COALESCE(s.ocr_text,''),'') IS NOT NULL THEN TRUE ELSE FALSE END AS "HasOcr", COALESCE(oj.status::text,'NONE') AS "OcrStatus",
CASE WHEN d.code ILIKE @QExact::text THEN 'CODE' WHEN d.title ILIKE @Q::text THEN 'TITLE' WHEN COALESCE(s.file_name,'') ILIKE @Q::text THEN 'FILE_NAME' WHEN COALESCE(d.description,'') ILIKE @Q::text THEN 'DESCRIPTION' ELSE 'OCR' END AS "MatchSource",
0::double precision AS "Rank",
CASE WHEN substring(COALESCE(s.ocr_text,'') from 1 for 4000) ILIKE @Q::text THEN substring(COALESCE(s.ocr_text,'') from 1 for 300)
WHEN COALESCE(d.description,'') ILIKE @Q::text THEN substring(COALESCE(d.description,'') from 1 for 300)
ELSE regexp_replace(COALESCE(d.title,s.file_name,''), @RawQ::text, '<mark>' || @RawQ::text || '</mark>', 'ig') END AS "Snippet",
CASE WHEN d.code ILIKE @QExact::text THEN 100 WHEN d.title ILIKE @Q::text THEN 86 WHEN COALESCE(s.file_name,'') ILIKE @Q::text THEN 70 WHEN substring(COALESCE(s.ocr_text,'') from 1 for 4000) ILIKE @Q::text THEN 74 WHEN COALESCE(d.description,'') ILIKE @Q::text THEN 60 ELSE 40 END AS "MatchScore"
FROM ged.document d
LEFT JOIN ged.document_search s ON s.tenant_id=d.tenant_id AND s.document_id=d.id
LEFT JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.id=s.version_id
LEFT JOIN ged.folder f ON f.tenant_id=d.tenant_id AND f.id=d.folder_id
LEFT JOIN LATERAL (SELECT vx.* FROM ged.document_version vx WHERE vx.tenant_id=d.tenant_id AND vx.document_id=d.id ORDER BY vx.version_number DESC,vx.created_at DESC LIMIT 1) latest_v ON true
LEFT JOIN LATERAL (SELECT j.status FROM ged.ocr_job j WHERE j.tenant_id=d.tenant_id AND j.document_version_id=COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) ORDER BY j.requested_at DESC LIMIT 1) oj ON true
WHERE d.tenant_id=@TenantId::uuid AND d.reg_status='A'::bpchar AND d.status<>'ARCHIVED'::ged.document_status_enum
AND (d.code ILIKE @Q::text OR d.title ILIKE @Q::text OR COALESCE(d.description,'') ILIKE @Q::text OR COALESCE(s.file_name,'') ILIKE @Q::text OR substring(COALESCE(s.ocr_text,'') from 1 for 4000) ILIKE @Q::text)
AND COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) IS NOT NULL
ORDER BY "MatchScore" DESC, "CreatedAt" DESC LIMIT 16;
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<HospitalDocumentSuggestionRow>(new CommandDefinition(sql, new { TenantId = tenantId, Q = like, RawQ = query, QExact = query }, cancellationToken: ct));
            var items = rows.Where(x => x.VersionId != EmptyGuid).Select(MapSuggestion).ToList();
            _cache.Set(cacheKey, items, TimeSpan.FromSeconds(45));
            _logger.LogInformation("Hospital suggestions executado. Tenant={TenantId} User={UserId} Query={Query} ResultCount={ResultCount} ElapsedMs={ElapsedMs} CacheHit={CacheHit}", tenantId, userId, query, items.Count, sw.ElapsedMilliseconds, false);
            return Json(new { success = true, items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em HospitalDocuments/Suggestions. Tenant={TenantId} User={UserId} Query={Query} ElapsedMs={ElapsedMs}", tenantId, userId, query, sw.ElapsedMilliseconds);
            return Json(new { success = false, items = Array.Empty<object>(), message = "Não foi possível carregar sugestões." });
        }
    }

    private HospitalDocumentResultDto MapResult(HospitalDocumentSearchRow x) => new() { DocumentId = x.DocumentId, VersionId = x.VersionId, Code = x.Code, Title = x.Title, FileName = x.FileName, ContentType = x.ContentType, Type = GetFriendlyType(x.ContentType, x.FileName), FolderName = x.FolderName, FolderPath = x.FolderPath, CreatedAt = x.CreatedAt, CreatedAtFormatted = x.CreatedAt.ToString("dd/MM/yyyy HH:mm"), SizeBytes = x.SizeBytes, SizeFormatted = FormatBytes(x.SizeBytes), HasOcr = !string.IsNullOrWhiteSpace(x.OcrText), OcrStatus = x.OcrStatus, MatchSource = x.MatchSource, MatchSourceLabel = GetMatchSourceLabel(x.MatchSource), MatchScore = x.MatchScore, Snippet = string.IsNullOrWhiteSpace(x.Snippet) ? "Documento encontrado pelos metadados informados." : x.Snippet, PreviewAvailable = IsPdf(x.ContentType, x.FileName) || IsImage(x.ContentType, x.FileName), PreviewUrl = Url.Action(nameof(Preview), "HospitalDocuments", new { versionId = x.VersionId }) ?? "", ViewerUrl = Url.Action(nameof(Viewer), "HospitalDocuments", new { versionId = x.VersionId }) ?? "", OcrUrl = Url.Action(nameof(OcrText), "HospitalDocuments", new { versionId = x.VersionId }) ?? "" };
    private HospitalDocumentSuggestionDto MapSuggestion(HospitalDocumentSuggestionRow x)
    {
        var source = (x.MatchSource ?? string.Empty).ToUpperInvariant();
        var group = source switch
        {
            "OCR" => "OCR / Conteúdo",
            "CODE" => "Prontuários / números",
            "FILE_NAME" or "TITLE" => "Documentos",
            "DESCRIPTION" => "Metadados",
            _ => "Documentos"
        };
        var friendlyType = GetFriendlyType(x.ContentType, x.FileName);
        var safeSnippet = string.IsNullOrWhiteSpace(x.Snippet) ? string.Empty : (x.Snippet.Length > 180 ? x.Snippet[..180] + "…" : x.Snippet);
        return new()
        {
            Group = group,
            SuggestionType = source == "OCR" ? "ocr" : "document",
            Icon = source == "OCR" ? "bi-body-text" : (friendlyType == "PDF" ? "bi-file-earmark-pdf" : "bi-file-earmark-text"),
            Score = x.MatchScore,
            Url = Url.Action(nameof(Viewer), "HospitalDocuments", new { versionId = x.VersionId }) ?? "",
            Subtitle = $"Pasta: {x.FolderPath ?? x.FolderName} · {friendlyType}",
            DocumentId = x.DocumentId,
            VersionId = x.VersionId,
            Code = x.Code,
            Title = x.Title,
            FileName = x.FileName,
            ContentType = x.ContentType,
            Type = source == "OCR" ? "ocr" : "document",
            FriendlyType = friendlyType,
            FolderName = x.FolderName,
            FolderPath = x.FolderPath,
            CreatedAt = x.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            Size = FormatBytes(x.SizeBytes),
            HasOcr = x.HasOcr,
            OcrStatus = x.OcrStatus,
            MatchSource = x.MatchSource,
            MatchSourceLabel = GetMatchSourceLabel(x.MatchSource),
            Snippet = safeSnippet,
            Label = $"{x.Code} - {x.Title}",
            Description = $"{friendlyType} · {x.FileName}",
            PreviewUrl = Url.Action(nameof(Preview), "HospitalDocuments", new { versionId = x.VersionId }) ?? "",
            ViewerUrl = Url.Action(nameof(Viewer), "HospitalDocuments", new { versionId = x.VersionId }) ?? "",
            OcrUrl = Url.Action(nameof(OcrText), "HospitalDocuments", new { versionId = x.VersionId }) ?? ""
        };
    }

    [HttpGet]
    public async Task<IActionResult> Viewer(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || versionId == Guid.Empty) return RedirectToAction(nameof(Index));
        await RegisterHospitalAccessAuditAsync("viewer", ct, new { versionId });
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ViewerRow>(new CommandDefinition("""SELECT d.id AS "DocumentId", dv.id AS "VersionId", COALESCE(NULLIF(d.code,''),d.id::text) AS "Code", COALESCE(NULLIF(d.title,''),'Documento sem título') AS "Title", COALESCE(NULLIF(dv.file_name,''),'arquivo') AS "FileName", COALESCE(NULLIF(dv.content_type,''),'') AS "ContentType", COALESCE(dv.file_size_bytes,0) AS "SizeBytes", d.created_at AS "CreatedAt", COALESCE(dv.storage_path,'') AS "StoragePath", COALESCE(s.ocr_text,'') AS "OcrText" FROM ged.document_version dv JOIN ged.document d ON d.tenant_id=dv.tenant_id AND d.id=dv.document_id LEFT JOIN ged.document_search s ON s.tenant_id=dv.tenant_id AND s.document_id=dv.document_id AND s.version_id=dv.id WHERE dv.tenant_id=@tenantId AND dv.id=@versionId AND d.reg_status='A'::bpchar LIMIT 1""", new { tenantId = _currentUser.TenantId, versionId }, cancellationToken: ct));
        if (row is null) return RedirectToAction(nameof(Index));
        return View(new HospitalDocumentViewerVM { DocumentId = row.DocumentId, VersionId = row.VersionId, Code = row.Code, Title = row.Title, FileName = row.FileName, ContentType = row.ContentType, TypeName = GetFriendlyType(row.ContentType, row.FileName), SizeBytes = row.SizeBytes, SizeFormatted = FormatBytes(row.SizeBytes), CreatedAt = row.CreatedAt, OcrText = row.OcrText, PreviewUrl = Url.Action(nameof(Preview), new { versionId }) ?? "", OcrUrl = Url.Action(nameof(OcrText), new { versionId }) ?? "" });
    }
    [HttpGet]
    public async Task<IActionResult> Preview(Guid versionId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var range = Request.Headers.Range.ToString();
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            if (versionId == Guid.Empty) return NotFound();
            await RegisterHospitalAccessAuditAsync("preview", ct, new { versionId });

            await using var conn = await _db.OpenAsync(ct);
            var row = await conn.QuerySingleOrDefaultAsync<ViewerRow>(new CommandDefinition("""SELECT dv.document_id AS "DocumentId", dv.id AS "VersionId", COALESCE(NULLIF(dv.file_name,''),'arquivo') AS "FileName", COALESCE(NULLIF(dv.content_type,''),'') AS "ContentType", COALESCE(dv.storage_path,'') AS "StoragePath", COALESCE(dv.file_size_bytes,0) AS "SizeBytes" FROM ged.document_version dv JOIN ged.document d ON d.tenant_id=dv.tenant_id AND d.id=dv.document_id WHERE dv.tenant_id=@tenantId AND dv.id=@versionId AND d.reg_status='A'::bpchar LIMIT 1""", new { tenantId, versionId }, cancellationToken: ct));
            if (row is null || string.IsNullOrWhiteSpace(row.StoragePath)) return NotFound();
            if (!await _storage.ExistsAsync(row.StoragePath, ct)) return NotFound();

            Response.Headers[HeaderNames.CacheControl] = "private, max-age=120";
            Response.Headers[HeaderNames.LastModified] = DateTimeOffset.UtcNow.ToString("R");

            if (IsImage(row.ContentType, row.FileName))
            {
                var image = await _storage.OpenReadAsync(row.StoragePath, ct);
                Response.Headers[HeaderNames.ContentDisposition] = $"inline; filename=\"{row.FileName}\"";
                _logger.LogInformation("Hospital preview streaming. Tenant={TenantId} User={UserId} VersionId={VersionId} FileSize={FileSize} ContentType={ContentType} Range={Range} ElapsedMs={ElapsedMs} Aborted={Aborted}", tenantId, userId, versionId, row.SizeBytes, row.ContentType, range, sw.ElapsedMilliseconds, false);
                return File(image, string.IsNullOrWhiteSpace(row.ContentType) ? "image/*" : row.ContentType, enableRangeProcessing: true);
            }

            if (IsPdf(row.ContentType, row.FileName))
            {
                var pdf = await _storage.OpenReadAsync(row.StoragePath, ct);
                Response.Headers[HeaderNames.ContentDisposition] = $"inline; filename=\"{row.FileName}\"";
                _logger.LogInformation("Hospital preview streaming. Tenant={TenantId} User={UserId} VersionId={VersionId} FileSize={FileSize} ContentType={ContentType} Range={Range} ElapsedMs={ElapsedMs} Aborted={Aborted}", tenantId, userId, versionId, row.SizeBytes, "application/pdf", range, sw.ElapsedMilliseconds, false);
                return File(pdf, "application/pdf", enableRangeProcessing: true);
            }

            var previewPath = BuildPreviewPath(tenantId, row.DocumentId, row.VersionId, row.FileName);
            if (!await _storage.ExistsAsync(previewPath, ct))
            {
                _logger.LogInformation("Hospital preview ainda não gerado; conversão não será feita no request. Tenant={TenantId} User={UserId} VersionId={VersionId} Range={Range} ElapsedMs={ElapsedMs}", tenantId, userId, versionId, range, sw.ElapsedMilliseconds);
                return PreviewProcessingContent();
            }
            var preview = await _storage.OpenReadAsync(previewPath, ct);
            Response.Headers[HeaderNames.ContentDisposition] = $"inline; filename=\"{Path.GetFileNameWithoutExtension(row.FileName)}.pdf\"";
            _logger.LogInformation("Hospital preview PDF streaming. Tenant={TenantId} User={UserId} VersionId={VersionId} FileSize={FileSize} ContentType={ContentType} Range={Range} ElapsedMs={ElapsedMs} Aborted={Aborted}", tenantId, userId, versionId, row.SizeBytes, "application/pdf", range, sw.ElapsedMilliseconds, false);
            return File(preview, "application/pdf", enableRangeProcessing: true);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Hospital preview abortado pelo cliente. Tenant={TenantId} User={UserId} VersionId={VersionId} Range={Range} ElapsedMs={ElapsedMs} Aborted={Aborted}", tenantId, userId, versionId, range, sw.ElapsedMilliseconds, true);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preview ainda indisponível para versão {VersionId}. Tenant={TenantId} User={UserId} Range={Range} ElapsedMs={ElapsedMs}", versionId, tenantId, userId, range, sw.ElapsedMilliseconds);
            return PreviewProcessingContent();
        }
    }
    [HttpGet] public async Task<IActionResult> OcrText(Guid versionId, CancellationToken ct) { if (!_currentUser.IsAuthenticated || versionId == Guid.Empty) return Json(new { success = false, hasOcr = false, text = "" }); await RegisterHospitalAccessAuditAsync("ocr_text", ct, new { versionId }); await using var conn = await _db.OpenAsync(ct); var row = await conn.QuerySingleOrDefaultAsync<OcrRow>(new CommandDefinition("""SELECT COALESCE(s.ocr_text,'') AS "Text", COALESCE(oj.status::text,'NONE') AS "Status" FROM ged.document_version dv LEFT JOIN ged.document_search s ON s.tenant_id=dv.tenant_id AND s.document_id=dv.document_id AND s.version_id=dv.id LEFT JOIN LATERAL (SELECT j.status FROM ged.ocr_job j WHERE j.tenant_id=dv.tenant_id AND j.document_version_id=dv.id ORDER BY j.requested_at DESC LIMIT 1) oj ON true WHERE dv.tenant_id=@tenantId AND dv.id=@versionId LIMIT 1""", new { tenantId = _currentUser.TenantId, versionId }, cancellationToken: ct)); return Json(new { success = true, hasOcr = !string.IsNullOrWhiteSpace(row?.Text), status = row?.Status ?? "NONE", text = row?.Text ?? "" }); }
    
    private async Task RegisterHospitalAccessAuditAsync(string action, CancellationToken ct, object? data = null)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == Guid.Empty || _currentUser.TenantId == Guid.Empty) return;
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "HTTP", "hospital_documents_access", null, $"HospitalDocuments:{action}", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), data, ct);
    }
    private ContentResult PreviewProcessingContent() => Content("""<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;font-family:system-ui;background:#f8fafc;color:#334155}.box{max-width:760px;margin:10vh auto;background:#fff;border:1px solid #e2e8f0;border-radius:12px;padding:20px}</style></head><body><div class="box"><h3>Gerando visualização</h3><p>Este documento ainda está sendo preparado para preview. Atualize em alguns segundos.</p></div></body></html>""", "text/html", Encoding.UTF8);

    private static string BuildPreviewPath(Guid tenantId, Guid documentId, Guid versionId, string originalFileName)
    {
        var previewName = $"{Path.GetFileNameWithoutExtension(SanitizePreviewFileName(originalFileName))}.pdf";
        return Path.Combine(tenantId.ToString("N"), "previews", documentId.ToString("N"), versionId.ToString("N"), previewName).Replace('\\', '/');
    }

    private static string SanitizePreviewFileName(string fileName)
    {
        fileName = string.IsNullOrWhiteSpace(fileName) ? "documento" : fileName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return string.IsNullOrWhiteSpace(fileName) ? "documento" : fileName;
    }

    private static string? NormalizeOcrStatus(string? status) => string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant() switch { "with" or "com" or "com_ocr" => "with", "without" or "sem" or "sem_ocr" => "without", "pending" => "PENDING", "processing" => "PROCESSING", "error" => "ERROR", "done" or "completed" => "DONE", _ => null };
    private static string NormalizeSort(string? sort) => string.IsNullOrWhiteSpace(sort) ? "relevance" : sort.Trim().ToLowerInvariant() switch { "recent" or "recentes" => "recent", "az" or "name" => "az", "type" or "tipo" => "type", _ => "relevance" };
    private static Dictionary<string, int> DeserializeTypeTotals(string? value) { if (string.IsNullOrWhiteSpace(value)) return new(); try { return JsonSerializer.Deserialize<Dictionary<string, int>>(value) ?? new(); } catch { return new(); } }
    private static string? NormalizeType(string? type) => string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant() switch { "pdf" => "pdf", "word" => "word", "doc" => "word", "docx" => "word", "imagem" => "image", "image" => "image", "img" => "image", _ => null };
    private static bool IsPdf(string? contentType, string fileName) => (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)) || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    private static bool IsImage(string? contentType, string fileName) => (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) || new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".tif", ".tiff" }.Contains(Path.GetExtension(fileName).ToLowerInvariant());
    private static string GetFriendlyType(string? contentType, string fileName) => IsPdf(contentType, fileName) ? "PDF" : IsImage(contentType, fileName) ? "Imagem" : (Path.GetExtension(fileName).ToLowerInvariant() is ".doc" or ".docx" ? "Documento Word" : "Documento");
    private static string GetMatchSourceLabel(string? matchSource) => (matchSource ?? "").ToUpperInvariant() switch { "CODE" => "Encontrado no código", "TITLE" => "Encontrado no título", "FILE_NAME" => "Encontrado no arquivo", "OCR" => "Encontrado no OCR", "DESCRIPTION" => "Encontrado na descrição", _ => "Encontrado por relevância" };
    private static string FormatBytes(long bytes) { if (bytes <= 0) return "Não informado"; string[] sizes = ["B", "KB", "MB", "GB"]; double len = bytes; var o = 0; while (len >= 1024 && o < sizes.Length - 1) { o++; len /= 1024; } return $"{len:0.##} {sizes[o]}"; }

    private sealed class HospitalDocumentSearchResultDto { public bool Success { get; set; } public IReadOnlyList<HospitalDocumentResultDto> Items { get; set; } = Array.Empty<HospitalDocumentResultDto>(); public int TotalResults { get; set; } public int ReturnedCount { get; set; } public int TotalWithOcr { get; set; } public int TotalWithoutOcr { get; set; } public Dictionary<string, int> TotalByType { get; set; } = new(); public long ElapsedMs { get; set; } public string Query { get; set; } = ""; public int Page { get; set; } public int PageSize { get; set; } public int TotalPages { get; set; } public bool HasMore { get; set; } }
    private sealed class HospitalDocumentSummaryDto { public bool Success { get; set; } public int TotalDocuments { get; set; } public int TotalWithOcr { get; set; } public int TotalWithoutOcr { get; set; } public int TotalTypes { get; set; } public DateTime? LatestIndexedAt { get; set; } }
    private sealed class HospitalDocumentSearchRow { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public long SizeBytes { get; set; } public DateTime CreatedAt { get; set; } public string Snippet { get; set; } = ""; public string OcrText { get; set; } = ""; public double Rank { get; set; } public int TotalRows { get; set; } public int TotalWithOcr { get; set; } public int TotalWithoutOcr { get; set; } public string TotalByType { get; set; } = "{}"; public string MatchSource { get; set; } = ""; public int MatchScore { get; set; } public string OcrStatus { get; set; } = ""; public string FolderName { get; set; } = ""; public string? FolderPath { get; set; } }
    private sealed class HospitalDocumentSuggestionRow { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public long SizeBytes { get; set; } public DateTime CreatedAt { get; set; } public string FolderName { get; set; } = ""; public string? FolderPath { get; set; } public bool HasOcr { get; set; } public string OcrStatus { get; set; } = ""; public string MatchSource { get; set; } = ""; public string Snippet { get; set; } = ""; public double Rank { get; set; } public int MatchScore { get; set; } }
    private sealed class HospitalDocumentResultDto { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public string Type { get; set; } = ""; public string FriendlyType { get; set; } = ""; public string FolderName { get; set; } = ""; public string? FolderPath { get; set; } public DateTime CreatedAt { get; set; } public string CreatedAtFormatted { get; set; } = ""; public long SizeBytes { get; set; } public string SizeFormatted { get; set; } = ""; public bool HasOcr { get; set; } public string OcrStatus { get; set; } = ""; public string MatchSource { get; set; } = ""; public string MatchSourceLabel { get; set; } = ""; public int MatchScore { get; set; } public string Snippet { get; set; } = ""; public bool PreviewAvailable { get; set; } public string PreviewUrl { get; set; } = ""; public string ViewerUrl { get; set; } = ""; public string OcrUrl { get; set; } = ""; }
    private sealed class HospitalDocumentSuggestionDto { public string Group { get; set; } = "Documentos"; public string SuggestionType { get; set; } = "document"; public string Subtitle { get; set; } = ""; public string Icon { get; set; } = "bi-file-earmark-text"; public string Url { get; set; } = ""; public int Score { get; set; } public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public string Type { get; set; } = ""; public string FriendlyType { get; set; } = ""; public string FolderName { get; set; } = ""; public string? FolderPath { get; set; } public string CreatedAt { get; set; } = ""; public string Size { get; set; } = ""; public bool HasOcr { get; set; } public string OcrStatus { get; set; } = ""; public string MatchSource { get; set; } = ""; public string MatchSourceLabel { get; set; } = ""; public string Snippet { get; set; } = ""; public string Label { get; set; } = ""; public string Description { get; set; } = ""; public string PreviewUrl { get; set; } = ""; public string ViewerUrl { get; set; } = ""; public string OcrUrl { get; set; } = ""; }
    private sealed class ViewerRow { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public long SizeBytes { get; set; } public DateTime CreatedAt { get; set; } public string StoragePath { get; set; } = ""; public string OcrText { get; set; } = ""; }
    private sealed class OcrRow { public string Text { get; set; } = ""; public string Status { get; set; } = "NONE"; }
}
