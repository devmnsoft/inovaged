using System.Text;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.HospitalDocuments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class HospitalDocumentsController : Controller
{
    private static readonly Guid EmptyGuid = Guid.Empty;
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IPreviewGenerator _preview;
    private readonly ILogger<HospitalDocumentsController> _logger;

    public HospitalDocumentsController(IDbConnectionFactory db, ICurrentUser currentUser, IFileStorage storage, IPreviewGenerator preview, ILogger<HospitalDocumentsController> logger)
    { _db = db; _currentUser = currentUser; _storage = storage; _preview = preview; _logger = logger; }

    [HttpGet] public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Search(string? q, string? type, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Json(new { success = true, total = 0, page, pageSize, hasMore = false, items = Array.Empty<object>() });

        var tenantId = _currentUser.TenantId; var query = q.Trim(); var likeQuery = $"%{query}%"; var normalizedType = NormalizeType(type); page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 5, 50);

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
), filtered AS (
SELECT * FROM base WHERE "VersionId" IS NOT NULL AND "VersionId" <> '00000000-0000-0000-0000-000000000000'::uuid
)
SELECT filtered.*, count(*) OVER()::int AS "TotalRows"
FROM filtered
ORDER BY "MatchScore" DESC, "Rank" DESC, "CreatedAt" DESC
LIMIT @pageSize OFFSET @offset;
""";
        try {
            await using var conn = await _db.OpenAsync(ct);
            var offset = (page - 1) * pageSize;
            var rows = (await conn.QueryAsync<HospitalDocumentSearchRow>(new CommandDefinition(sql, new { tenantId, q = query, likeQuery, qExact = query, docType = normalizedType, offset, pageSize }, cancellationToken: ct))).ToList();
            var total = rows.FirstOrDefault()?.TotalRows ?? 0;
            var items = rows.Where(x => x.VersionId != EmptyGuid).Select(MapResult).ToList();
            var hasMore = offset + rows.Count < total;
            return Json(new { success = true, total, page, pageSize, hasMore, items });
        } catch (Exception ex) { _logger.LogError(ex, "Erro na busca hospitalar."); return StatusCode(500, new { success = false, message = "Erro ao pesquisar documentos hospitalares." }); }
    }

    [HttpGet]
    public async Task<IActionResult> Suggestions(string? q, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Json(Array.Empty<object>());
        var tenantId = _currentUser.TenantId; var query = q.Trim(); var like = $"%{query}%";
        const string sql = """
SELECT d.id AS "DocumentId", COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) AS "VersionId",
COALESCE(NULLIF(d.code,''),d.id::text) AS "Code", COALESCE(NULLIF(d.title,''),'Documento sem título') AS "Title", COALESCE(NULLIF(s.file_name,''),NULLIF(v.file_name,''),NULLIF(latest_v.file_name,''),'arquivo') AS "FileName",
COALESCE(NULLIF(v.content_type,''),NULLIF(latest_v.content_type,''),'') AS "ContentType", COALESCE(v.file_size_bytes,latest_v.file_size_bytes,0) AS "SizeBytes", d.created_at AS "CreatedAt", COALESCE(f.name,'Sem pasta') AS "FolderName", NULL::text AS "FolderPath",
CASE WHEN NULLIF(COALESCE(s.ocr_text,''),'') IS NOT NULL THEN TRUE ELSE FALSE END AS "HasOcr", COALESCE(oj.status::text,'NONE') AS "OcrStatus",
CASE WHEN d.code ILIKE @qExact THEN 'CODE' WHEN d.title ILIKE @q THEN 'TITLE' WHEN COALESCE(s.file_name,'') ILIKE @q THEN 'FILE_NAME' WHEN COALESCE(d.description,'') ILIKE @q THEN 'DESCRIPTION' ELSE 'OCR' END AS "MatchSource",
CASE WHEN s.search_vector IS NOT NULL THEN ts_rank(s.search_vector, websearch_to_tsquery('portuguese', @rawq)) ELSE 0 END AS "Rank",
CASE WHEN COALESCE(s.ocr_text,'') ILIKE @q THEN regexp_replace(substring(COALESCE(s.ocr_text,'') from '.{0,80}' || @rawq || '.{0,80}'), @rawq, '<mark>' || @rawq || '</mark>', 'ig')
WHEN COALESCE(d.description,'') ILIKE @q THEN regexp_replace(substring(COALESCE(d.description,'') from '.{0,80}' || @rawq || '.{0,80}'), @rawq, '<mark>' || @rawq || '</mark>', 'ig')
ELSE regexp_replace(COALESCE(d.title,s.file_name,''), @rawq, '<mark>' || @rawq || '</mark>', 'ig') END AS "Snippet",
CASE WHEN d.code ILIKE @qExact THEN 100 WHEN d.title ILIKE @q THEN 86 WHEN COALESCE(s.file_name,'') ILIKE @q THEN 70 WHEN COALESCE(s.ocr_text,'') ILIKE @q THEN 74 WHEN COALESCE(d.description,'') ILIKE @q THEN 60 ELSE 40 END AS "MatchScore"
FROM ged.document d
LEFT JOIN ged.document_search s ON s.tenant_id=d.tenant_id AND s.document_id=d.id
LEFT JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.id=s.version_id
LEFT JOIN ged.folder f ON f.tenant_id=d.tenant_id AND f.id=d.folder_id
LEFT JOIN LATERAL (SELECT vx.* FROM ged.document_version vx WHERE vx.tenant_id=d.tenant_id AND vx.document_id=d.id ORDER BY vx.version_number DESC,vx.created_at DESC LIMIT 1) latest_v ON true
LEFT JOIN LATERAL (SELECT j.status FROM ged.ocr_job j WHERE j.tenant_id=d.tenant_id AND j.document_version_id=COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) ORDER BY j.requested_at DESC LIMIT 1) oj ON true
WHERE d.tenant_id=@tenantId AND d.reg_status='A'::bpchar AND d.status<>'ARCHIVED'::ged.document_status_enum
AND (d.code ILIKE @q OR d.title ILIKE @q OR COALESCE(d.description,'') ILIKE @q OR COALESCE(s.file_name,'') ILIKE @q OR COALESCE(s.ocr_text,'') ILIKE @q)
AND COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) IS NOT NULL
ORDER BY "MatchScore" DESC, "Rank" DESC, "CreatedAt" DESC LIMIT 10;
""";
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<HospitalDocumentSuggestionRow>(new CommandDefinition(sql, new { tenantId, q = like, rawq = query, qExact = query }, cancellationToken: ct));
        return Json(rows.Where(x => x.VersionId != EmptyGuid).Select(MapSuggestion));
    }

    private HospitalDocumentResultDto MapResult(HospitalDocumentSearchRow x) => new() { DocumentId = x.DocumentId, VersionId = x.VersionId, Code = x.Code, Title = x.Title, FileName = x.FileName, ContentType = x.ContentType, Type = GetFriendlyType(x.ContentType, x.FileName), FolderName = x.FolderName, FolderPath = x.FolderPath, CreatedAt = x.CreatedAt, CreatedAtFormatted = x.CreatedAt.ToString("dd/MM/yyyy HH:mm"), SizeBytes = x.SizeBytes, SizeFormatted = FormatBytes(x.SizeBytes), HasOcr = !string.IsNullOrWhiteSpace(x.OcrText), OcrStatus = x.OcrStatus, MatchSource = x.MatchSource, MatchSourceLabel = GetMatchSourceLabel(x.MatchSource), MatchScore = x.MatchScore, Snippet = string.IsNullOrWhiteSpace(x.Snippet) ? "Documento encontrado pelos metadados informados." : x.Snippet, PreviewAvailable = IsPdf(x.ContentType, x.FileName) || IsImage(x.ContentType, x.FileName), PreviewUrl = Url.Action(nameof(Preview), "HospitalDocuments", new { versionId = x.VersionId }) ?? "", ViewerUrl = Url.Action(nameof(Viewer), "HospitalDocuments", new { versionId = x.VersionId }) ?? "", OcrUrl = Url.Action(nameof(OcrText), "HospitalDocuments", new { versionId = x.VersionId }) ?? "" };
    private HospitalDocumentSuggestionDto MapSuggestion(HospitalDocumentSuggestionRow x) => new() { DocumentId = x.DocumentId, VersionId = x.VersionId, Code = x.Code, Title = x.Title, FileName = x.FileName, ContentType = x.ContentType, Type = GetFriendlyType(x.ContentType, x.FileName), FolderName = x.FolderName, FolderPath = x.FolderPath, CreatedAt = x.CreatedAt.ToString("dd/MM/yyyy HH:mm"), Size = FormatBytes(x.SizeBytes), HasOcr = x.HasOcr, OcrStatus = x.OcrStatus, MatchSource = x.MatchSource, MatchSourceLabel = GetMatchSourceLabel(x.MatchSource), Snippet = x.Snippet, Label = $"{x.Code} - {x.Title}", Description = $"{GetFriendlyType(x.ContentType, x.FileName)} · {x.FileName}", PreviewUrl = Url.Action(nameof(Preview), "HospitalDocuments", new { versionId = x.VersionId }) ?? "", ViewerUrl = Url.Action(nameof(Viewer), "HospitalDocuments", new { versionId = x.VersionId }) ?? "", OcrUrl = Url.Action(nameof(OcrText), "HospitalDocuments", new { versionId = x.VersionId }) ?? "" };

    [HttpGet]
    public async Task<IActionResult> Viewer(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || versionId == Guid.Empty) return RedirectToAction(nameof(Index));
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ViewerRow>(new CommandDefinition("""SELECT d.id AS "DocumentId", dv.id AS "VersionId", COALESCE(NULLIF(d.code,''),d.id::text) AS "Code", COALESCE(NULLIF(d.title,''),'Documento sem título') AS "Title", COALESCE(NULLIF(dv.file_name,''),'arquivo') AS "FileName", COALESCE(NULLIF(dv.content_type,''),'') AS "ContentType", COALESCE(dv.file_size_bytes,0) AS "SizeBytes", d.created_at AS "CreatedAt", COALESCE(dv.storage_path,'') AS "StoragePath", COALESCE(s.ocr_text,'') AS "OcrText" FROM ged.document_version dv JOIN ged.document d ON d.tenant_id=dv.tenant_id AND d.id=dv.document_id LEFT JOIN ged.document_search s ON s.tenant_id=dv.tenant_id AND s.document_id=dv.document_id AND s.version_id=dv.id WHERE dv.tenant_id=@tenantId AND dv.id=@versionId AND d.reg_status='A'::bpchar LIMIT 1""", new { tenantId = _currentUser.TenantId, versionId }, cancellationToken: ct));
        if (row is null) return RedirectToAction(nameof(Index));
        return View(new HospitalDocumentViewerVM { DocumentId = row.DocumentId, VersionId = row.VersionId, Code = row.Code, Title = row.Title, FileName = row.FileName, ContentType = row.ContentType, TypeName = GetFriendlyType(row.ContentType, row.FileName), SizeBytes = row.SizeBytes, SizeFormatted = FormatBytes(row.SizeBytes), CreatedAt = row.CreatedAt, OcrText = row.OcrText, PreviewUrl = Url.Action(nameof(Preview), new { versionId }) ?? "", OcrUrl = Url.Action(nameof(OcrText), new { versionId }) ?? "" });
    }
    [HttpGet]
    public async Task<IActionResult> Preview(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (versionId == Guid.Empty) return NotFound();

        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ViewerRow>(new CommandDefinition("""SELECT dv.document_id AS "DocumentId", dv.id AS "VersionId", COALESCE(NULLIF(dv.file_name,''),'arquivo') AS "FileName", COALESCE(NULLIF(dv.content_type,''),'') AS "ContentType", COALESCE(dv.storage_path,'') AS "StoragePath" FROM ged.document_version dv JOIN ged.document d ON d.tenant_id=dv.tenant_id AND d.id=dv.document_id WHERE dv.tenant_id=@tenantId AND dv.id=@versionId AND d.reg_status='A'::bpchar LIMIT 1""", new { tenantId = _currentUser.TenantId, versionId }, cancellationToken: ct));
        if (row is null || string.IsNullOrWhiteSpace(row.StoragePath)) return NotFound();
        if (!await _storage.ExistsAsync(row.StoragePath, ct)) return NotFound();

        if (IsImage(row.ContentType, row.FileName))
        {
            var image = await _storage.OpenReadAsync(row.StoragePath, ct);
            Response.Headers[HeaderNames.ContentDisposition] = $"inline; filename=\"{row.FileName}\"";
            return File(image, string.IsNullOrWhiteSpace(row.ContentType) ? "image/*" : row.ContentType, enableRangeProcessing: true);
        }

        if (IsPdf(row.ContentType, row.FileName))
        {
            var pdf = await _storage.OpenReadAsync(row.StoragePath, ct);
            Response.Headers[HeaderNames.ContentDisposition] = $"inline; filename=\"{row.FileName}\"";
            return File(pdf, "application/pdf", enableRangeProcessing: true);
        }

        try
        {
            var previewPath = await _preview.GetOrCreatePreviewPdfAsync(_currentUser.TenantId, row.DocumentId, row.VersionId, row.StoragePath, row.FileName, ct);
            if (!await _storage.ExistsAsync(previewPath, ct)) return PreviewProcessingContent();
            var preview = await _storage.OpenReadAsync(previewPath, ct);
            Response.Headers[HeaderNames.ContentDisposition] = $"inline; filename=\"{Path.GetFileNameWithoutExtension(row.FileName)}.pdf\"";
            return File(preview, "application/pdf", enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preview ainda indisponível para versão {VersionId}.", versionId);
            return PreviewProcessingContent();
        }
    }
    [HttpGet] public async Task<IActionResult> OcrText(Guid versionId, CancellationToken ct) { if (!_currentUser.IsAuthenticated || versionId == Guid.Empty) return Json(new { success = false, hasOcr = false, text = "" }); await using var conn = await _db.OpenAsync(ct); var row = await conn.QuerySingleOrDefaultAsync<OcrRow>(new CommandDefinition("""SELECT COALESCE(s.ocr_text,'') AS "Text", COALESCE(oj.status::text,'NONE') AS "Status" FROM ged.document_version dv LEFT JOIN ged.document_search s ON s.tenant_id=dv.tenant_id AND s.document_id=dv.document_id AND s.version_id=dv.id LEFT JOIN LATERAL (SELECT j.status FROM ged.ocr_job j WHERE j.tenant_id=dv.tenant_id AND j.document_version_id=dv.id ORDER BY j.requested_at DESC LIMIT 1) oj ON true WHERE dv.tenant_id=@tenantId AND dv.id=@versionId LIMIT 1""", new { tenantId = _currentUser.TenantId, versionId }, cancellationToken: ct)); return Json(new { success = true, hasOcr = !string.IsNullOrWhiteSpace(row?.Text), status = row?.Status ?? "NONE", text = row?.Text ?? "" }); }
    private ContentResult PreviewProcessingContent() => Content("""<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;font-family:system-ui;background:#f8fafc;color:#334155}.box{max-width:760px;margin:10vh auto;background:#fff;border:1px solid #e2e8f0;border-radius:12px;padding:20px}</style></head><body><div class="box"><h3>Gerando visualização</h3><p>Este documento ainda está sendo preparado para preview. Atualize em alguns segundos.</p></div></body></html>""", "text/html", Encoding.UTF8);

    private static string? NormalizeType(string? type) => string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant() switch { "pdf" => "pdf", "word" => "word", "doc" => "word", "docx" => "word", "imagem" => "image", "image" => "image", "img" => "image", _ => null };
    private static bool IsPdf(string? contentType, string fileName) => (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)) || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    private static bool IsImage(string? contentType, string fileName) => (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) || new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".tif", ".tiff" }.Contains(Path.GetExtension(fileName).ToLowerInvariant());
    private static string GetFriendlyType(string? contentType, string fileName) => IsPdf(contentType, fileName) ? "PDF" : IsImage(contentType, fileName) ? "Imagem" : (Path.GetExtension(fileName).ToLowerInvariant() is ".doc" or ".docx" ? "Documento Word" : "Documento");
    private static string GetMatchSourceLabel(string? matchSource) => (matchSource ?? "").ToUpperInvariant() switch { "CODE" => "Encontrado no código", "TITLE" => "Encontrado no título", "FILE_NAME" => "Encontrado no arquivo", "OCR" => "Encontrado no OCR", "DESCRIPTION" => "Encontrado na descrição", _ => "Encontrado por relevância" };
    private static string FormatBytes(long bytes) { if (bytes <= 0) return "Não informado"; string[] sizes = ["B", "KB", "MB", "GB"]; double len = bytes; var o = 0; while (len >= 1024 && o < sizes.Length - 1) { o++; len /= 1024; } return $"{len:0.##} {sizes[o]}"; }

    private sealed class HospitalDocumentSearchRow { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public long SizeBytes { get; set; } public DateTime CreatedAt { get; set; } public string Snippet { get; set; } = ""; public string OcrText { get; set; } = ""; public double Rank { get; set; } public int TotalRows { get; set; } public string MatchSource { get; set; } = ""; public int MatchScore { get; set; } public string OcrStatus { get; set; } = ""; public string FolderName { get; set; } = ""; public string? FolderPath { get; set; } }
    private sealed class HospitalDocumentSuggestionRow { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public long SizeBytes { get; set; } public DateTime CreatedAt { get; set; } public string FolderName { get; set; } = ""; public string? FolderPath { get; set; } public bool HasOcr { get; set; } public string OcrStatus { get; set; } = ""; public string MatchSource { get; set; } = ""; public string Snippet { get; set; } = ""; public double Rank { get; set; } public int MatchScore { get; set; } }
    private sealed class HospitalDocumentResultDto { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public string Type { get; set; } = ""; public string FolderName { get; set; } = ""; public string? FolderPath { get; set; } public DateTime CreatedAt { get; set; } public string CreatedAtFormatted { get; set; } = ""; public long SizeBytes { get; set; } public string SizeFormatted { get; set; } = ""; public bool HasOcr { get; set; } public string OcrStatus { get; set; } = ""; public string MatchSource { get; set; } = ""; public string MatchSourceLabel { get; set; } = ""; public int MatchScore { get; set; } public string Snippet { get; set; } = ""; public bool PreviewAvailable { get; set; } public string PreviewUrl { get; set; } = ""; public string ViewerUrl { get; set; } = ""; public string OcrUrl { get; set; } = ""; }
    private sealed class HospitalDocumentSuggestionDto { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public string Type { get; set; } = ""; public string FolderName { get; set; } = ""; public string? FolderPath { get; set; } public string CreatedAt { get; set; } = ""; public string Size { get; set; } = ""; public bool HasOcr { get; set; } public string OcrStatus { get; set; } = ""; public string MatchSource { get; set; } = ""; public string MatchSourceLabel { get; set; } = ""; public string Snippet { get; set; } = ""; public string Label { get; set; } = ""; public string Description { get; set; } = ""; public string PreviewUrl { get; set; } = ""; public string ViewerUrl { get; set; } = ""; public string OcrUrl { get; set; } = ""; }
    private sealed class ViewerRow { public Guid DocumentId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public long SizeBytes { get; set; } public DateTime CreatedAt { get; set; } public string StoragePath { get; set; } = ""; public string OcrText { get; set; } = ""; }
    private sealed class OcrRow { public string Text { get; set; } = ""; public string Status { get; set; } = "NONE"; }
}
