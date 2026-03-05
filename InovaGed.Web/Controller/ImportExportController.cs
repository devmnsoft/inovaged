using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("ImportExport")]
public sealed class ImportExportController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentContext _ctx;
    private readonly DocumentAppService _documentApp;
    private readonly ILogger<ImportExportController> _logger;

    public ImportExportController(
        IDbConnectionFactory db,
        ICurrentContext ctx,
        DocumentAppService documentApp,
        ILogger<ImportExportController> logger)
    {
        _db = db;
        _ctx = ctx;
        _documentApp = documentApp;
        _logger = logger;
    }

    private Guid TenantId => _ctx.TenantId;
    private Guid UserId => _ctx.UserId;

    // =========================================================
    // GET /ImportExport
    // =========================================================
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var recents = (await conn.QueryAsync<ImportLogRow>(
            """
            SELECT
                il.id            AS Id,
                il.file_name     AS FileName,
                il.imported_at   AS ImportedAt,
                il.status        AS Status,
                il.sig_detected  AS SigDetected,
                il.sig_status    AS SigStatus,
                il.sig_details   AS SigDetails,
                d.id             AS DocumentId,
                d.code           AS DocumentCode,
                d.title          AS DocumentTitle
            FROM ged.import_log il
            LEFT JOIN ged.document d ON d.id = il.document_id
            WHERE il.tenant_id = @tenant
            ORDER BY il.imported_at DESC
            LIMIT 50
            """,
            new { tenant = TenantId }
        )).ToList();

        var folders = (await conn.QueryAsync<FolderRow>(
            """
            SELECT id AS Id, name AS Name
            FROM ged.folder
            WHERE tenant_id = @tenant
              AND reg_status = 'A'
            ORDER BY name
            """,
            new { tenant = TenantId }
        )).ToList();

        return View(new ImportIndexVM(recents, folders));
    }

    // =========================================================
    // POST /ImportExport/Import — Item 22 PoC
    // =========================================================
    [HttpPost("Import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<IActionResult> Import(
        [FromForm] IFormFile file,
        [FromForm] Guid folderId,
        [FromForm] string? title,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Err"] = "Selecione um arquivo para importar.";
            return RedirectToAction(nameof(Index));
        }

        var logId = Guid.NewGuid();
        var fileName = file.FileName;

        bool sigDetected = false;
        string sigStatus = "NOT_SIGNED"; // apenas para o import_log
        string? sigDetails = null;
        Guid? documentId = null;

        try
        {
            // 1) Ler bytes em memória
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            ms.Position = 0;
            var fileBytes = ms.ToArray();

            // 2) Detectar assinatura (PoC)
            if (IsPdf(fileName, file.ContentType))
            {
                (sigDetected, sigStatus, sigDetails) = DetectPdfSignature(fileBytes);
            }

            // 3) Upload no GED
            ms.Position = 0;
            var docTitle = string.IsNullOrWhiteSpace(title)
                ? Path.GetFileNameWithoutExtension(fileName)
                : title.Trim();

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ua = Request.Headers.UserAgent.ToString();

            var cmd = new UploadDocumentCommand
            {
                FolderId = folderId,
                Title = docTitle,
                FileName = fileName,
                ContentType = file.ContentType,
                Content = ms
            };

            var uploadResult = await _documentApp.UploadAsync(cmd, ip, ua, ct);

            if (!uploadResult.Success)
            {
                TempData["Err"] = $"Falha ao importar: {uploadResult.Error?.Message}";
                await WriteImportLogAsync(logId, fileName, "FAILED", sigDetected, sigStatus, sigDetails, null, ct);
                return RedirectToAction(nameof(Index));
            }

            documentId = uploadResult.Value;

            // 4) Registrar assinatura detectada (somente se detectou)
            //    - Para a tabela document_signature o enum é ged.signature_status
            //    - Como é PoC sem validação completa, gravamos UNKNOWN
            if (sigDetected)
            {
                await RegisterImportSignatureAsync(documentId.Value, "UNKNOWN", sigDetails, ct);
            }

            // 5) Log
            await WriteImportLogAsync(logId, fileName, "SUCCESS", sigDetected, sigStatus, sigDetails, documentId, ct);

            TempData["Ok"] = sigDetected
                ? $"Documento importado. Assinatura digital detectada: {sigStatus}."
                : "Documento importado sem assinatura digital.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na importação. File={File}", fileName);
            TempData["Err"] = "Erro inesperado durante a importação.";
            await WriteImportLogAsync(logId, fileName, "ERROR", sigDetected, sigStatus, sigDetails, documentId, ct);
            return RedirectToAction(nameof(Index));
        }
    }

    // =========================================================
    // GET /ImportExport/ExportCenter — Item 23 PoC
    // =========================================================
    [HttpGet("ExportCenter")]
    public async Task<IActionResult> ExportCenter(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var docs = (await conn.QueryAsync<ExportDocRow>(
            """
            SELECT
              dv.id AS Id,                      -- ✅ IMPORTANTE: Id da VERSION (para Export)
              d.code AS Code,
              d.title AS Title,
              dv.file_name AS FileName,
              dv.content_type AS ContentType,
              dv.file_size_bytes AS SizeBytes,
              s.status::text AS SigStatus
            FROM ged.document d
            JOIN ged.document_version dv
              ON dv.id = d.current_version_id
            LEFT JOIN ged.vw_document_latest_signature s
              ON s.tenant_id = d.tenant_id
             AND s.document_id = d.id
            WHERE d.tenant_id = @tenant
              AND d.status <> 'DELETED'::ged.document_status_enum
            ORDER BY d.created_at DESC
            LIMIT 100
            """,
            new { tenant = TenantId }
        )).ToList();

        return View("ExportCenter", docs);
    }

    // =========================================================
    // GET /ImportExport/Export?versionId=...
    // =========================================================
    [HttpGet("Export")]
    public async Task<IActionResult> Export([FromQuery] Guid versionId, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        // ✅ document_version NÃO tem reg_status (não filtrar)
        var v = await conn.QuerySingleOrDefaultAsync<VersionDownloadRow>(
            """
            SELECT storage_path AS StoragePath,
                   file_name    AS FileName,
                   content_type AS ContentType
            FROM ged.document_version
            WHERE tenant_id = @tenant
              AND id = @id
            """,
            new { tenant = TenantId, id = versionId }
        );

        if (v is null) return NotFound();

        // Se o seu GED/Download aceita versionId, mantém assim.
        // Se aceitar documentId, me diga e eu ajusto a rota/ação correta.
        return RedirectToAction("Download", "Ged", new { id = versionId });
    }

    // =========================================================
    // Helpers privados
    // =========================================================

    /// <summary>
    /// Detecta assinatura digital em PDF (PoC). Procura marcadores comuns:
    /// /ByteRange e /Sig /DocTimeStamp /ETSI.CAdES.
    /// Não valida cadeia/certificado: apenas detecta.
    /// </summary>
    private static (bool detected, string status, string? details) DetectPdfSignature(byte[] pdfBytes)
    {
        try
        {
            var pdfText = System.Text.Encoding.Latin1.GetString(pdfBytes);

            bool hasByteRange = pdfText.Contains("/ByteRange");
            bool hasSigField = pdfText.Contains("/Sig")
                            || pdfText.Contains("/DocTimeStamp")
                            || pdfText.Contains("/ETSI.CAdES");

            if (!hasByteRange && !hasSigField)
                return (false, "NOT_SIGNED", null);

            var details = new List<string>();
            if (hasByteRange) details.Add("ByteRange presente");
            if (hasSigField) details.Add("Campo Sig detectado");

            return (true, "UNKNOWN", string.Join("; ", details) + " — validação completa requer cadeia ICP-Brasil");
        }
        catch
        {
            return (false, "NOT_SIGNED", "Erro ao inspecionar PDF");
        }
    }

    private static bool IsPdf(string fileName, string contentType)
        => fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
           || contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    private async Task RegisterImportSignatureAsync(Guid documentId, string status, string? details, CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(
                """
                INSERT INTO ged.document_signature
                    (id, tenant_id, document_id, signed_by, signed_by_name, cpf,
                     cert_subject, cert_issuer, cert_serial,
                     signing_time, status, status_details,
                     signature_bytes, reg_date, reg_status)
                VALUES
                    (gen_random_uuid(), @tenant, @documentId, @userId, 'IMPORTAÇÃO', '',
                     '', '', '',
                     now(), @status::ged.signature_status, @details,
                     ''::bytea, now(), 'A')
                """,
                new
                {
                    tenant = TenantId,
                    documentId,
                    userId = UserId,
                    status, // ✅ VALID | INVALID | NOT_VERIFIABLE | UNKNOWN
                    details = details ?? "Assinatura detectada na importação"
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível registrar assinatura da importação. Doc={Doc}", documentId);
        }
    }

    private async Task WriteImportLogAsync(
        Guid logId, string fileName, string status,
        bool sigDetected, string sigStatus, string? sigDetails,
        Guid? documentId, CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO ged.import_log
                    (id, tenant_id, document_id, file_name, imported_by,
                     imported_at, status, sig_detected, sig_status, sig_details,
                     reg_date, reg_status)
                VALUES
                    (@id, @tenant, @documentId, @fileName, @userId,
                     now(), @status, @sigDetected, @sigStatus, @sigDetails,
                     now(), 'A')
                ON CONFLICT DO NOTHING
                """,
                new
                {
                    id = logId,
                    tenant = TenantId,
                    documentId,
                    fileName,
                    userId = UserId,
                    status,
                    sigDetected,
                    sigStatus,
                    sigDetails
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gravar import_log. Id={Id}", logId);
        }
    }

    // =========================================================
    // ViewModels
    // =========================================================

    public sealed record ImportIndexVM(List<ImportLogRow> RecentImports, List<FolderRow> Folders);

    public sealed record ImportLogRow(
        Guid Id,
        string FileName,
        DateTime ImportedAt,
        string Status,
        bool SigDetected,
        string SigStatus,
        string? SigDetails,
        Guid? DocumentId,
        string? DocumentCode,
        string? DocumentTitle);

    public sealed record FolderRow(Guid Id, string Name);

    public sealed record ExportDocRow(
        Guid Id,              // ✅ aqui é o dv.id (versionId)
        string Code,
        string Title,
        string FileName,
        string? ContentType,
        long SizeBytes,
        string? SigStatus);

    public sealed record VersionDownloadRow(
        string StoragePath,
        string FileName,
        string ContentType);
}