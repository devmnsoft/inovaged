using System.Text;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Identity;
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

    public HospitalDocumentsController(
        IDbConnectionFactory db,
        ICurrentUser currentUser,
        IFileStorage storage,
        IPreviewGenerator preview,
        ILogger<HospitalDocumentsController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _preview = preview;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        string? q,
        string? type,
        int limit = 30,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Json(new
            {
                success = true,
                total = 0,
                items = Array.Empty<object>(),
                message = "Digite pelo menos 2 caracteres."
            });
        }

        limit = Math.Clamp(limit, 1, 80);

        var tenantId = _currentUser.TenantId;
        var query = q.Trim();
        var likeQuery = $"%{query}%";
        var normalizedType = NormalizeType(type);

        const string sql = """
        WITH tokens AS (
            SELECT regexp_replace(lower(t), '[^[:alnum:]À-ÿ_]+', '', 'g') AS tok
            FROM regexp_split_to_table(trim(@q), '\s+') AS t
        ),
        query_ts AS (
            SELECT
                CASE
                    WHEN count(*) = 0 THEN NULL
                    ELSE to_tsquery('portuguese', string_agg(tok || ':*', ' & '))
                END AS tsq
            FROM tokens
            WHERE tok <> ''
        ),
        base AS (
            SELECT
                d.id AS "DocumentId",

                COALESCE(
                    NULLIF(s.version_id, '00000000-0000-0000-0000-000000000000'::uuid),
                    NULLIF(d.current_version_id, '00000000-0000-0000-0000-000000000000'::uuid),
                    latest_v.id
                ) AS "VersionId",

                COALESCE(NULLIF(d.code, ''), d.id::text) AS "Code",
                COALESCE(NULLIF(d.title, ''), 'Documento sem título') AS "Title",

                COALESCE(
                    NULLIF(s.file_name, ''),
                    NULLIF(v.file_name, ''),
                    NULLIF(latest_v.file_name, ''),
                    'arquivo'
                ) AS "FileName",

                COALESCE(
                    NULLIF(v.content_type, ''),
                    NULLIF(latest_v.content_type, ''),
                    ''
                ) AS "ContentType",

                0::bigint AS "SizeBytes",
                d.created_at AS "CreatedAt",

                COALESCE(s.ocr_text, '') AS "OcrText",

                CASE
                    WHEN s.search_vector IS NOT NULL
                         AND (SELECT tsq FROM query_ts) IS NOT NULL
                         AND s.search_vector @@ (SELECT tsq FROM query_ts)
                    THEN ts_headline(
                        'portuguese',
                        COALESCE(s.ocr_text, d.description, d.title, s.file_name, ''),
                        (SELECT tsq FROM query_ts),
                        'StartSel=<mark>, StopSel=</mark>, MaxFragments=2, FragmentDelimiter= … , MaxWords=22, MinWords=8'
                    )
                    ELSE COALESCE(NULLIF(d.description, ''), NULLIF(s.file_name, ''), NULLIF(d.title, ''), 'Documento encontrado pelos metadados.')
                END AS "Snippet",

                CASE
                    WHEN s.search_vector IS NOT NULL
                         AND (SELECT tsq FROM query_ts) IS NOT NULL
                         AND s.search_vector @@ (SELECT tsq FROM query_ts)
                    THEN ts_rank(s.search_vector, (SELECT tsq FROM query_ts))
                    ELSE 0
                END AS "Rank"

            FROM ged.document d

            LEFT JOIN ged.document_search s
              ON s.tenant_id = d.tenant_id
             AND s.document_id = d.id

            LEFT JOIN ged.document_version v
              ON v.tenant_id = d.tenant_id
             AND v.id = s.version_id

            LEFT JOIN LATERAL (
                SELECT vx.*
                FROM ged.document_version vx
                WHERE vx.tenant_id = d.tenant_id
                  AND vx.document_id = d.id
                ORDER BY vx.version_number DESC, vx.created_at DESC
                LIMIT 1
            ) latest_v ON true

            WHERE d.tenant_id = @tenantId

              AND (
                    d.code ILIKE @likeQuery
                    OR d.title ILIKE @likeQuery
                    OR COALESCE(d.description, '') ILIKE @likeQuery
                    OR COALESCE(s.file_name, '') ILIKE @likeQuery
                    OR COALESCE(s.ocr_text, '') ILIKE @likeQuery
                    OR (
                        s.search_vector IS NOT NULL
                        AND (SELECT tsq FROM query_ts) IS NOT NULL
                        AND s.search_vector @@ (SELECT tsq FROM query_ts)
                    )
              )

              AND (
                    @docType IS NULL

                    OR (
                        @docType = 'pdf'
                        AND (
                            lower(COALESCE(v.content_type, latest_v.content_type, '')) LIKE '%pdf%'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.pdf'
                        )
                    )

                    OR (
                        @docType = 'word'
                        AND (
                            lower(COALESCE(v.content_type, latest_v.content_type, '')) LIKE '%word%'
                            OR lower(COALESCE(v.content_type, latest_v.content_type, '')) LIKE '%officedocument.wordprocessingml.document%'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.doc'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.docx'
                        )
                    )

                    OR (
                        @docType = 'image'
                        AND (
                            lower(COALESCE(v.content_type, latest_v.content_type, '')) LIKE 'image/%'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.jpg'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.jpeg'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.png'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.tif'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.tiff'
                            OR lower(COALESCE(s.file_name, v.file_name, latest_v.file_name, '')) LIKE '%.webp'
                        )
                    )
              )
        )
        SELECT *
        FROM base
        WHERE "VersionId" IS NOT NULL
          AND "VersionId" <> '00000000-0000-0000-0000-000000000000'::uuid
        ORDER BY "Rank" DESC, "CreatedAt" DESC
        LIMIT @limit;
        """;

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var rows = await conn.QueryAsync<HospitalDocumentSearchRow>(
                new CommandDefinition(sql, new
                {
                    tenantId,
                    q = query,
                    likeQuery,
                    docType = normalizedType,
                    limit
                }, cancellationToken: ct));

            var list = rows
                .Where(x => x.VersionId != EmptyGuid)
                .Select(x => new
                {
                    documentId = x.DocumentId,
                    versionId = x.VersionId,
                    code = x.Code,
                    title = x.Title,
                    fileName = x.FileName,
                    contentType = x.ContentType,
                    size = FormatBytes(x.SizeBytes),
                    createdAt = x.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    type = GetFriendlyType(x.ContentType, x.FileName),
                    snippet = string.IsNullOrWhiteSpace(x.Snippet)
                        ? "Documento encontrado pelos metadados informados."
                        : x.Snippet,
                    hasOcr = !string.IsNullOrWhiteSpace(x.OcrText),
                    previewUrl = Url.Action(nameof(Preview), "HospitalDocuments", new { versionId = x.VersionId }),
                    ocrUrl = Url.Action(nameof(OcrText), "HospitalDocuments", new { versionId = x.VersionId })
                })
                .ToList();

            return Json(new
            {
                success = true,
                total = list.Count,
                items = list
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na busca hospitalar. Tenant={TenantId}, Query={Query}", tenantId, query);

            return StatusCode(500, new
            {
                success = false,
                message = "Erro ao pesquisar documentos hospitalares.",
                detail = ex.Message
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Suggestions(string? q, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Json(Array.Empty<object>());

        var tenantId = _currentUser.TenantId;
        var query = $"%{q.Trim()}%";

        const string sql = """
        SELECT DISTINCT
            d.id AS "DocumentId",

            COALESCE(
                NULLIF(s.version_id, '00000000-0000-0000-0000-000000000000'::uuid),
                NULLIF(d.current_version_id, '00000000-0000-0000-0000-000000000000'::uuid),
                latest_v.id
            ) AS "VersionId",

            COALESCE(NULLIF(d.code, ''), d.id::text) AS "Code",
            COALESCE(NULLIF(d.title, ''), 'Documento sem título') AS "Title",

            COALESCE(
                NULLIF(s.file_name, ''),
                NULLIF(v.file_name, ''),
                NULLIF(latest_v.file_name, ''),
                'arquivo'
            ) AS "FileName",

            COALESCE(
                NULLIF(v.content_type, ''),
                NULLIF(latest_v.content_type, ''),
                ''
            ) AS "ContentType"

        FROM ged.document d

        LEFT JOIN ged.document_search s
          ON s.tenant_id = d.tenant_id
         AND s.document_id = d.id

        LEFT JOIN ged.document_version v
          ON v.tenant_id = d.tenant_id
         AND v.id = s.version_id

        LEFT JOIN LATERAL (
            SELECT vx.*
            FROM ged.document_version vx
            WHERE vx.tenant_id = d.tenant_id
              AND vx.document_id = d.id
            ORDER BY vx.version_number DESC, vx.created_at DESC
            LIMIT 1
        ) latest_v ON true

        WHERE d.tenant_id = @tenantId
          AND (
                d.code ILIKE @q
                OR d.title ILIKE @q
                OR COALESCE(d.description, '') ILIKE @q
                OR COALESCE(s.file_name, '') ILIKE @q
                OR COALESCE(s.ocr_text, '') ILIKE @q
          )
          AND COALESCE(
                NULLIF(s.version_id, '00000000-0000-0000-0000-000000000000'::uuid),
                NULLIF(d.current_version_id, '00000000-0000-0000-0000-000000000000'::uuid),
                latest_v.id
              ) IS NOT NULL

        ORDER BY "Title"
        LIMIT 10;
        """;

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var rows = await conn.QueryAsync<HospitalDocumentSuggestionRow>(
                new CommandDefinition(sql, new { tenantId, q = query }, cancellationToken: ct));

            var result = rows
                .Where(x => x.VersionId != EmptyGuid)
                .Select(x => new
                {
                    documentId = x.DocumentId,
                    versionId = x.VersionId,
                    label = $"{x.Code} - {x.Title}",
                    description = $"{GetFriendlyType(x.ContentType, x.FileName)} · {x.FileName}"
                });

            return Json(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar sugestões hospitalares.");
            return Json(Array.Empty<object>());
        }
    }

    [HttpGet]
    public async Task<IActionResult> Viewer(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        if (versionId == EmptyGuid)
            return BadRequest("Versão inválida.");

        var tenantId = _currentUser.TenantId;

        const string sql = """
        SELECT
            d.id AS "DocumentId",
            v.id AS "VersionId",
            COALESCE(NULLIF(d.code, ''), d.id::text) AS "Code",
            COALESCE(NULLIF(d.title, ''), 'Documento sem título') AS "Title",
            COALESCE(v.file_name, 'arquivo') AS "FileName",
            COALESCE(v.content_type, '') AS "ContentType",
            0::bigint AS "SizeBytes",
            d.created_at AS "CreatedAt",
            COALESCE(s.ocr_text, '') AS "OcrText"
        FROM ged.document_version v
        JOIN ged.document d
          ON d.tenant_id = v.tenant_id
         AND d.id = v.document_id
        LEFT JOIN ged.document_search s
          ON s.tenant_id = v.tenant_id
         AND s.version_id = v.id
        WHERE v.tenant_id = @tenantId
          AND v.id = @versionId
        LIMIT 1;
        """;

        await using var conn = await _db.OpenAsync(ct);

        var doc = await conn.QueryFirstOrDefaultAsync<HospitalDocumentViewerVM>(
            new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));

        if (doc is null)
            return NotFound("Documento não encontrado.");

        doc.TypeName = GetFriendlyType(doc.ContentType, doc.FileName);
        doc.SizeFormatted = FormatBytes(doc.SizeBytes);
        doc.PreviewUrl = Url.Action(nameof(Preview), "HospitalDocuments", new { versionId }) ?? "";
        doc.OcrUrl = Url.Action(nameof(OcrText), "HospitalDocuments", new { versionId }) ?? "";

        await RegisterViewAuditAsync(conn, tenantId, doc.DocumentId, versionId, ct);

        return View(doc);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        if (versionId == EmptyGuid)
            return Content(BuildPreviewErrorHtml("Versão inválida", "O documento não possui uma versão válida para visualização."), "text/html", Encoding.UTF8);

        var tenantId = _currentUser.TenantId;

        const string sql = """
        SELECT
            v.document_id AS "DocumentId",
            v.id AS "VersionId",
            COALESCE(v.file_name, 'arquivo') AS "FileName",
            COALESCE(v.content_type, '') AS "ContentType",
            v.storage_path AS "StoragePath"
        FROM ged.document_version v
        WHERE v.tenant_id = @tenantId
          AND v.id = @versionId
        LIMIT 1;
        """;

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var v = await conn.QueryFirstOrDefaultAsync<HospitalDocumentVersionRow>(
                new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));

            if (v is null)
                return Content(BuildPreviewErrorHtml("Documento não encontrado", "A versão solicitada não foi localizada no banco de dados."), "text/html", Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(v.StoragePath))
                return Content(BuildPreviewErrorHtml("Arquivo sem caminho", "A versão do documento não possui caminho de armazenamento."), "text/html", Encoding.UTF8);

            if (!await _storage.ExistsAsync(v.StoragePath, ct))
                return Content(BuildPreviewErrorHtml("Arquivo não encontrado", $"O arquivo não foi localizado no storage: {v.StoragePath}"), "text/html", Encoding.UTF8);

            if (IsImage(v.ContentType, v.FileName))
            {
                var img = await _storage.OpenReadAsync(v.StoragePath, ct);
                SetInlineContentDisposition(v.FileName);

                return File(
                    img,
                    string.IsNullOrWhiteSpace(v.ContentType) ? "image/*" : v.ContentType,
                    enableRangeProcessing: true
                );
            }

            if (IsPdf(v.ContentType, v.FileName))
            {
                var pdf = await _storage.OpenReadAsync(v.StoragePath, ct);
                SetInlineContentDisposition(v.FileName);

                return File(pdf, "application/pdf", enableRangeProcessing: true);
            }

            var previewPath = await _preview.GetOrCreatePreviewPdfAsync(
                tenantId,
                v.DocumentId,
                versionId,
                v.StoragePath,
                v.FileName,
                ct);

            if (!await _storage.ExistsAsync(previewPath, ct))
                return Content(BuildGeneratingPreviewHtml(versionId), "text/html", Encoding.UTF8);

            var preview = await _storage.OpenReadAsync(previewPath, ct);
            var previewName = $"{Path.GetFileNameWithoutExtension(v.FileName)}.pdf";

            SetInlineContentDisposition(previewName);

            return File(preview, "application/pdf", enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar preview hospitalar. VersionId={VersionId}", versionId);

            return Content(
                BuildPreviewErrorHtml("Erro ao abrir visualização", ex.Message),
                "text/html",
                Encoding.UTF8
            );
        }
    }

    [HttpGet]
    public async Task<IActionResult> OcrText(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        if (versionId == EmptyGuid)
        {
            return Json(new
            {
                success = false,
                hasOcr = false,
                text = "",
                message = "Versão inválida."
            });
        }

        var tenantId = _currentUser.TenantId;

        const string sql = """
        SELECT COALESCE(s.ocr_text, '') AS "OcrText"
        FROM ged.document_search s
        WHERE s.tenant_id = @tenantId
          AND s.version_id = @versionId
        LIMIT 1;
        """;

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var text = await conn.ExecuteScalarAsync<string>(
                new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));

            return Json(new
            {
                success = true,
                hasOcr = !string.IsNullOrWhiteSpace(text),
                text = text ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar OCR hospitalar. VersionId={VersionId}", versionId);

            return Json(new
            {
                success = false,
                hasOcr = false,
                text = "",
                message = "Erro ao carregar OCR."
            });
        }
    }

    private async Task RegisterViewAuditAsync(
        System.Data.IDbConnection conn,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken ct)
    {
        try
        {
            const string sql = """
            INSERT INTO ged.audit_log
            (
                id,
                tenant_id,
                entity,
                entity_id,
                action,
                details,
                user_id,
                created_at
            )
            VALUES
            (
                gen_random_uuid(),
                @tenantId,
                'document',
                @documentId,
                'HOSPITAL_DOCUMENT_VIEW',
                @details::jsonb,
                @userId,
                now()
            );
            """;

            var details = $$"""
            {
                "versionId": "{{versionId}}",
                "origin": "HospitalDocuments",
                "ip": "{{HttpContext.Connection.RemoteIpAddress}}",
                "userAgent": "{{Request.Headers.UserAgent.ToString().Replace("\"", "'")}}"
            }
            """;

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenantId,
                documentId,
                userId = _currentUser.UserId,
                details
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível gravar auditoria de visualização hospitalar.");
        }
    }

    private static string? NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return null;

        var t = type.Trim().ToLowerInvariant();

        return t switch
        {
            "pdf" => "pdf",
            "word" => "word",
            "doc" => "word",
            "docx" => "word",
            "imagem" => "image",
            "image" => "image",
            "img" => "image",
            _ => null
        };
    }

    private static bool IsPdf(string? contentType, string fileName)
    {
        return (!string.IsNullOrWhiteSpace(contentType)
                && contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
               || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".tif" or ".tiff";
    }

    private static string GetFriendlyType(string? contentType, string fileName)
    {
        if (IsPdf(contentType, fileName))
            return "PDF";

        if (IsImage(contentType, fileName))
            return "Imagem";

        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (ext is ".doc" or ".docx")
            return "Documento Word";

        return string.IsNullOrWhiteSpace(ext)
            ? "Documento"
            : ext.Replace(".", "").ToUpperInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "Não informado";

        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        var order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private void SetInlineContentDisposition(string fileName)
    {
        var cd = new ContentDispositionHeaderValue("inline");
        cd.SetHttpFileName(fileName);
        Response.Headers[HeaderNames.ContentDisposition] = cd.ToString();
    }

    private string BuildGeneratingPreviewHtml(Guid versionId)
    {
        var retryUrl = Url.Action(nameof(Preview), "HospitalDocuments", new { versionId }) ?? "#";

        return $$"""
        <!doctype html>
        <html lang="pt-br">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Gerando visualização</title>
            <style>
                body {
                    margin: 0;
                    font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                    background: #f3f6fb;
                    color: #1f2937;
                }

                .box {
                    max-width: 680px;
                    margin: 12vh auto;
                    background: #fff;
                    border-radius: 18px;
                    padding: 28px;
                    box-shadow: 0 18px 45px rgba(15, 23, 42, .12);
                    text-align: center;
                }

                .loader {
                    width: 42px;
                    height: 42px;
                    border-radius: 50%;
                    border: 4px solid #dbeafe;
                    border-top-color: #2563eb;
                    margin: 0 auto 16px;
                    animation: spin .8s linear infinite;
                }

                @keyframes spin {
                    to { transform: rotate(360deg); }
                }

                .muted { color: #64748b; }
            </style>
        </head>
        <body>
            <div class="box">
                <div class="loader"></div>
                <h2>Preparando visualização do documento</h2>
                <p class="muted">O arquivo está sendo convertido para visualização segura em PDF.</p>
            </div>

            <script>
                setTimeout(function () {
                    location.href = "{{retryUrl}}";
                }, 2500);
            </script>
        </body>
        </html>
        """;
    }

    private static string BuildPreviewErrorHtml(string title, string message)
    {
        return $$"""
        <!doctype html>
        <html lang="pt-br">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>{{title}}</title>
            <style>
                body {
                    margin: 0;
                    font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                    background: #f8fafc;
                    color: #0f172a;
                }

                .box {
                    max-width: 760px;
                    margin: 10vh auto;
                    background: #fff;
                    border-radius: 22px;
                    padding: 30px;
                    box-shadow: 0 18px 45px rgba(15, 23, 42, .12);
                    border: 1px solid #e5e7eb;
                }

                .icon {
                    width: 56px;
                    height: 56px;
                    border-radius: 18px;
                    background: #fee2e2;
                    color: #991b1b;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    font-size: 28px;
                    font-weight: 900;
                    margin-bottom: 16px;
                }

                h2 { margin: 0 0 8px; }
                p { color: #64748b; line-height: 1.55; }
                code {
                    display: block;
                    margin-top: 12px;
                    background: #f1f5f9;
                    border-radius: 12px;
                    padding: 12px;
                    color: #334155;
                    white-space: pre-wrap;
                }
            </style>
        </head>
        <body>
            <div class="box">
                <div class="icon">!</div>
                <h2>{{System.Net.WebUtility.HtmlEncode(title)}}</h2>
                <p>Não foi possível exibir o documento solicitado.</p>
                <code>{{System.Net.WebUtility.HtmlEncode(message)}}</code>
            </div>
        </body>
        </html>
        """;
    }

    private sealed class HospitalDocumentSearchRow
    {
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Snippet { get; set; } = "";
        public string OcrText { get; set; } = "";
        public double Rank { get; set; }
    }

    private sealed class HospitalDocumentSuggestionRow
    {
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
    }

    private sealed class HospitalDocumentVersionRow
    {
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public string StoragePath { get; set; } = "";
    }
}

public sealed class HospitalDocumentViewerVM
{
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string OcrText { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public string OcrUrl { get; set; } = "";
}