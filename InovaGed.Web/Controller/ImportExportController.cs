using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    // Central de Importação
    // =========================================================
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
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
                LEFT JOIN ged.document d 
                       ON d.tenant_id = il.tenant_id
                      AND d.id = il.document_id
                WHERE il.tenant_id = @tenant
                  AND il.reg_status = 'A'
                ORDER BY il.imported_at DESC
                LIMIT 50
                """,
                new { tenant = TenantId }
            )).ToList();

            var folders = (await conn.QueryAsync<FolderRow>(
                """
                SELECT 
                    id   AS Id, 
                    name AS Name
                FROM ged.folder
                WHERE tenant_id = @tenant
                  AND reg_status = 'A'
                ORDER BY name
                """,
                new { tenant = TenantId }
            )).ToList();

            return View(new ImportIndexVM(recents, folders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar central de importação/exportação.");

            TempData["Err"] = "Erro ao carregar a central de importação/exportação.";

            return View(new ImportIndexVM(new List<ImportLogRow>(), new List<FolderRow>()));
        }
    }

    // =========================================================
    // POST /ImportExport/Import
    // Importação de documento com detecção de assinatura digital
    // =========================================================
    [HttpPost("Import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)]
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

        if (folderId == Guid.Empty)
        {
            TempData["Err"] = "Selecione uma pasta de destino para importar o documento.";
            return RedirectToAction(nameof(Index));
        }

        var logId = Guid.NewGuid();
        var fileName = Path.GetFileName(file.FileName);

        bool sigDetected = false;
        string sigStatus = "NOT_SIGNED";
        string? sigDetails = null;
        Guid? documentId = null;

        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);

            ms.Position = 0;
            var fileBytes = ms.ToArray();

            if (IsPdf(fileName, file.ContentType))
            {
                (sigDetected, sigStatus, sigDetails) = DetectPdfSignature(fileBytes);
            }

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
                ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                Content = ms
            };

            var uploadResult = await _documentApp.UploadAsync(cmd, ip, ua, ct);

            if (!uploadResult.Success)
            {
                var errorMessage = uploadResult.Error?.Message ?? "Falha não especificada no upload.";

                TempData["Err"] = $"Falha ao importar: {errorMessage}";

                await WriteImportLogAsync(
                    logId,
                    fileName,
                    "FAILED",
                    sigDetected,
                    sigStatus,
                    sigDetails,
                    null,
                    ct);

                await WriteOperationalLogAsync(
                    "IMPORT_FAILED",
                    "document",
                    null,
                    "Falha ao importar documento.",
                    "ERROR",
                    new
                    {
                        fileName,
                        folderId,
                        title = docTitle,
                        sigDetected,
                        sigStatus,
                        sigDetails,
                        error = errorMessage
                    },
                    ct);

                return RedirectToAction(nameof(Index));
            }

            documentId = uploadResult.Value;

            if (sigDetected && documentId.HasValue)
            {
                await RegisterImportSignatureAsync(
                    documentId.Value,
                    "UNKNOWN",
                    sigDetails,
                    ct);
            }

            await WriteImportLogAsync(
                logId,
                fileName,
                "SUCCESS",
                sigDetected,
                sigStatus,
                sigDetails,
                documentId,
                ct);

            await WriteOperationalLogAsync(
                "IMPORT",
                "document",
                documentId,
                "Documento importado com sucesso.",
                "INFO",
                new
                {
                    fileName,
                    folderId,
                    title = docTitle,
                    sigDetected,
                    sigStatus,
                    sigDetails,
                    documentId
                },
                ct);

            TempData["Ok"] = sigDetected
                ? $"Documento importado com sucesso. Assinatura digital detectada: {TranslateSignatureStatus(sigStatus)}."
                : "Documento importado com sucesso. Nenhuma assinatura digital foi detectada.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado durante importação. File={File}", fileName);

            TempData["Err"] = "Erro inesperado durante a importação. A ocorrência foi registrada no log.";

            await WriteImportLogAsync(
                logId,
                fileName,
                "ERROR",
                sigDetected,
                sigStatus,
                sigDetails,
                documentId,
                ct);

            await WriteOperationalLogAsync(
                "IMPORT_ERROR",
                "document",
                documentId,
                "Erro inesperado durante a importação.",
                "ERROR",
                new
                {
                    fileName,
                    folderId,
                    sigDetected,
                    sigStatus,
                    sigDetails,
                    error = ex.Message
                },
                ct);

            return RedirectToAction(nameof(Index));
        }
    }

    // =========================================================
    // GET /ImportExport/ExportCenter
    // Central de Exportação
    // =========================================================
    [HttpGet("ExportCenter")]
    public async Task<IActionResult> ExportCenter(CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            var docs = (await conn.QueryAsync<ExportDocRow>(
                """
                SELECT
                    dv.id                  AS Id,
                    d.code                 AS Code,
                    d.title                AS Title,
                    dv.file_name           AS FileName,
                    dv.content_type        AS ContentType,
                    dv.file_size_bytes     AS SizeBytes,
                    s.status::text         AS SigStatus
                FROM ged.document d
                JOIN ged.document_version dv
                  ON dv.tenant_id = d.tenant_id
                 AND dv.id = d.current_version_id
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar central de exportação.");

            TempData["Err"] = "Erro ao carregar a central de exportação.";

            return View("ExportCenter", new List<ExportDocRow>());
        }
    }

    // =========================================================
    // GET /ImportExport/Export?versionId=...
    // Exportação / Download
    // =========================================================
    [HttpGet("Export")]
    public async Task<IActionResult> Export([FromQuery] Guid versionId, CancellationToken ct)
    {
        if (versionId == Guid.Empty)
        {
            TempData["Err"] = "Versão do documento inválida para exportação.";
            return RedirectToAction(nameof(ExportCenter));
        }

        try
        {
            using var conn = await _db.OpenAsync(ct);

            var version = await conn.QuerySingleOrDefaultAsync<VersionDownloadRow>(
                """
                SELECT 
                    storage_path AS StoragePath,
                    file_name    AS FileName,
                    content_type AS ContentType
                FROM ged.document_version
                WHERE tenant_id = @tenant
                  AND id = @id
                """,
                new { tenant = TenantId, id = versionId }
            );

            if (version is null)
            {
                TempData["Err"] = "Documento não encontrado para exportação.";
                return RedirectToAction(nameof(ExportCenter));
            }

            await WriteOperationalLogAsync(
                "EXPORT",
                "document_version",
                versionId,
                "Documento exportado pela central de exportação.",
                "INFO",
                new
                {
                    versionId,
                    version.FileName,
                    version.ContentType,
                    version.StoragePath
                },
                ct);

            TempData["Ok"] = "Exportação registrada com sucesso.";

            return RedirectToAction("Download", "Ged", new { id = versionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao exportar documento. VersionId={VersionId}", versionId);

            await WriteOperationalLogAsync(
                "EXPORT_ERROR",
                "document_version",
                versionId,
                "Erro ao exportar documento.",
                "ERROR",
                new
                {
                    versionId,
                    error = ex.Message
                },
                ct);

            TempData["Err"] = "Erro ao exportar documento. A ocorrência foi registrada no log.";

            return RedirectToAction(nameof(ExportCenter));
        }
    }

    // =========================================================
    // Helpers privados
    // =========================================================

    private static (bool detected, string status, string? details) DetectPdfSignature(byte[] pdfBytes)
    {
        try
        {
            var pdfText = System.Text.Encoding.Latin1.GetString(pdfBytes);

            var hasByteRange = pdfText.Contains("/ByteRange", StringComparison.OrdinalIgnoreCase);

            var hasSigField =
                pdfText.Contains("/Sig", StringComparison.OrdinalIgnoreCase) ||
                pdfText.Contains("/DocTimeStamp", StringComparison.OrdinalIgnoreCase) ||
                pdfText.Contains("/ETSI.CAdES", StringComparison.OrdinalIgnoreCase) ||
                pdfText.Contains("/Adobe.PPKLite", StringComparison.OrdinalIgnoreCase);

            if (!hasByteRange && !hasSigField)
                return (false, "NOT_SIGNED", null);

            var details = new List<string>();

            if (hasByteRange)
                details.Add("ByteRange presente");

            if (hasSigField)
                details.Add("Campo de assinatura detectado");

            return (
                true,
                "UNKNOWN",
                string.Join("; ", details) + " — validação completa requer cadeia ICP-Brasil, LCR/OCSP e verificação criptográfica."
            );
        }
        catch
        {
            return (false, "NOT_SIGNED", "Erro ao inspecionar PDF.");
        }
    }

    private static bool IsPdf(string fileName, string? contentType)
    {
        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
               || string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string TranslateSignatureStatus(string status)
    {
        return status switch
        {
            "VALID" => "válida",
            "INVALID" => "inválida",
            "UNKNOWN" => "detectada, mas ainda sem verificação completa",
            "NOT_VERIFIABLE" => "não verificável",
            "NOT_SIGNED" => "não assinada",
            _ => status
        };
    }

    private async Task RegisterImportSignatureAsync(
        Guid documentId,
        string status,
        string? details,
        CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(
                """
                INSERT INTO ged.document_signature
                (
                    id,
                    tenant_id,
                    document_id,
                    signed_by,
                    signed_by_name,
                    cpf,
                    cert_subject,
                    cert_issuer,
                    cert_serial,
                    signing_time,
                    status,
                    status_details,
                    signature_bytes,
                    reg_date,
                    reg_status
                )
                VALUES
                (
                    gen_random_uuid(),
                    @tenant,
                    @documentId,
                    @userId,
                    'IMPORTAÇÃO',
                    '',
                    '',
                    '',
                    '',
                    now(),
                    @status::ged.signature_status,
                    @details,
                    ''::bytea,
                    now(),
                    'A'
                )
                """,
                new
                {
                    tenant = TenantId,
                    documentId,
                    userId = UserId,
                    status,
                    details = details ?? "Assinatura detectada durante a importação."
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Não foi possível registrar assinatura da importação. DocumentId={DocumentId}",
                documentId);
        }
    }

    private async Task WriteImportLogAsync(
        Guid logId,
        string fileName,
        string status,
        bool sigDetected,
        string sigStatus,
        string? sigDetails,
        Guid? documentId,
        CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(
                """
                INSERT INTO ged.import_log
                (
                    id,
                    tenant_id,
                    document_id,
                    file_name,
                    imported_by,
                    imported_at,
                    status,
                    sig_detected,
                    sig_status,
                    sig_details,
                    reg_date,
                    reg_status
                )
                VALUES
                (
                    @id,
                    @tenant,
                    @documentId,
                    @fileName,
                    @userId,
                    now(),
                    @status,
                    @sigDetected,
                    @sigStatus,
                    @sigDetails,
                    now(),
                    'A'
                )
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
            _logger.LogWarning(ex, "Falha ao gravar import_log. LogId={LogId}", logId);
        }
    }

    private async Task WriteOperationalLogAsync(
        string action,
        string entityName,
        Guid? entityId,
        string message,
        string severity,
        object? data,
        CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(
                """
                INSERT INTO ged.operational_event_log
                (
                    tenant_id,
                    user_id,
                    module,
                    entity_name,
                    entity_id,
                    action,
                    message,
                    severity,
                    ip_address,
                    user_agent,
                    data,
                    created_at,
                    reg_status
                )
                VALUES
                (
                    @tenant,
                    @user,
                    'IMPORT_EXPORT',
                    @entityName,
                    @entityId,
                    @action,
                    @message,
                    @severity,
                    @ip,
                    @ua,
                    @data::jsonb,
                    now(),
                    'A'
                )
                """,
                new
                {
                    tenant = TenantId,
                    user = UserId,
                    entityName,
                    entityId,
                    action,
                    message,
                    severity = string.IsNullOrWhiteSpace(severity) ? "INFO" : severity,
                    ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    ua = Request.Headers.UserAgent.ToString(),
                    data = JsonSerializer.Serialize(data ?? new { })
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Falha ao gravar log operacional. Action={Action}, Entity={EntityName}, EntityId={EntityId}",
                action,
                entityName,
                entityId);
        }
    }

    // =========================================================
    // ViewModels
    // =========================================================

    public sealed record ImportIndexVM(
        List<ImportLogRow> RecentImports,
        List<FolderRow> Folders);

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

    public sealed record FolderRow(
        Guid Id,
        string Name);

    public sealed record ExportDocRow(
        Guid Id,
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