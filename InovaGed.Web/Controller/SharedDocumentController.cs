using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Route("SharedDocument")]
public sealed class SharedDocumentController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SharedDocumentController> _logger;
    public SharedDocumentController(IDbConnectionFactory db, ILogger<SharedDocumentController> logger) { _db = db; _logger = logger; }

    [HttpGet("{token}")]
    public async Task<IActionResult> Index(string token, CancellationToken ct)
    {
        var link = await ValidateAsync(token, true, ct);
        if (link is null) return View("SharedDocumentDenied", ViewData["DeniedReason"]);
        return View("Index", link);
    }

    [HttpGet("{token}/Preview")]
    public async Task<IActionResult> Preview(string token, CancellationToken ct)
    {
        var link = await ValidateAsync(token, false, ct);
        if (link is null) return NotFound("Link inválido, expirado ou revogado.");
        if (!link.AllowPreview) return Content("Visualização não autorizada para este link.", "text/plain", Encoding.UTF8);
        await LogAccessAsync(link, true, "SECURE_DOCUMENT_LINK_PREVIEW", ct);
        return Content("Pré-visualização isolada habilitada apenas para este documento. Integre aqui o viewer existente usando DocumentId/VersionId.", "text/plain", Encoding.UTF8);
    }

    [HttpGet("{token}/Download")]
    public async Task<IActionResult> Download(string token, CancellationToken ct)
    {
        var link = await ValidateAsync(token, false, ct);
        if (link is null) return NotFound("Link inválido, expirado ou revogado.");
        if (!link.AllowDownload) return Content("Download não autorizado para este link.", "text/plain", Encoding.UTF8);
        await LogAccessAsync(link, true, "SECURE_DOCUMENT_LINK_DOWNLOAD", ct);
        return Content("Download autorizado apenas para o documento vinculado. Integre aqui o streaming do arquivo versionado GED.", "text/plain", Encoding.UTF8);
    }

    [HttpGet("{token}/OcrText")]
    public async Task<IActionResult> OcrText(string token, CancellationToken ct)
    {
        var link = await ValidateAsync(token, false, ct);
        if (link is null) return NotFound();
        var ocr = await LoadOcrAsync(link.TenantId, link.DocumentId, link.VersionId, ct);
        if (string.IsNullOrWhiteSpace(ocr)) return Content("Este documento ainda não possui OCR pesquisável.");
        return Content(Mask(ocr.Length > 4000 ? ocr[..4000] + "…" : ocr), "text/plain", Encoding.UTF8);
    }

    [HttpPost("{token}/Search")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Search(string token, string q, CancellationToken ct)
    {
        var link = await ValidateAsync(token, false, ct);
        if (link is null) return Json(new { success = false, message = "Link inválido, expirado ou revogado." });
        if (!link.AllowSmartSearch) return Json(new { success = false, message = "Busca textual desabilitada para este link." });
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3) return Json(new { success = false, message = "Digite pelo menos 3 caracteres." });
        var ocr = await LoadOcrAsync(link.TenantId, link.DocumentId, link.VersionId, ct);
        if (string.IsNullOrWhiteSpace(ocr)) return Json(new { success = true, items = Array.Empty<object>(), message = "Este documento ainda não possui OCR pesquisável." });
        var terms = Regex.Split(q ?? string.Empty, @"[^\p{L}\p{N}]+", RegexOptions.None).Where(x => x.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var snippets = terms.Select(t => BuildSnippet(ocr, t)).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Mask(x!)).Take(10).ToList();
        await LogAccessAsync(link, true, "SECURE_DOCUMENT_LINK_SEARCH", ct);
        return Json(new { success = true, items = snippets, message = snippets.Count == 0 ? "Não encontrei esse termo neste documento." : null });
    }

    private async Task<SharedDocumentLinkVm?> ValidateAsync(string token, bool countAccess, CancellationToken ct)
    {
        var hash = Sha256Token(token ?? string.Empty);
        await using var conn = await _db.OpenAsync(ct);
        var link = await conn.QuerySingleOrDefaultAsync<SharedDocumentLinkVm>(new CommandDefinition("""
select id as "Id", tenant_id as "TenantId", loan_request_id as "LoanRequestId", document_id as "DocumentId", version_id as "VersionId", title as "Title", description as "Description", expires_at as "ExpiresAt", is_permanent as "IsPermanent", max_access_count as "MaxAccessCount", access_count as "AccessCount", allow_preview as "AllowPreview", allow_smart_search as "AllowSmartSearch", allow_download as "AllowDownload", revoked_at as "RevokedAt"
from ged.secure_document_link where token_hash=@hash and reg_status='A'
""", new { hash }, cancellationToken: ct));
        if (link is null)
        {
            ViewData["DeniedReason"] = "Link inválido.";
            _logger.LogInformation("Acesso negado ao link seguro. Reason={Reason}", "INVALID_TOKEN");
            return null;
        }

        var reason = GetDeniedReason(link, PostgresDateTimeHelper.UtcNow());
        if (reason is not null)
        {
            ViewData["DeniedReason"] = FriendlyDeniedReason(reason);
            if (countAccess) await LogAccessAsync(link, false, reason, ct);
            return null;
        }

        if (countAccess)
        {
            var updated = await conn.ExecuteAsync(new CommandDefinition("""
update ged.secure_document_link
set access_count=access_count+1, last_access_at=now()
where id=@id and (max_access_count is null or access_count < max_access_count)
""", new { link.Id }, cancellationToken: ct));
            if (updated == 0)
            {
                ViewData["DeniedReason"] = FriendlyDeniedReason("ACCESS_LIMIT");
                await LogAccessAsync(link, false, "ACCESS_LIMIT", ct);
                return null;
            }

            await LogAccessAsync(link, true, "SECURE_DOCUMENT_LINK_OPENED", ct);
            link.AccessCount++;
        }

        return link;
    }

    private static string? GetDeniedReason(SharedDocumentLinkVm link, DateTimeOffset now)
    {
        if (link.RevokedAt is not null) return "REVOKED";
        if (!link.IsPermanent && link.ExpiresAt is null) return "INVALID_EXPIRATION";
        if (!link.IsPermanent && link.ExpiresAt <= now) return "EXPIRED";
        if (link.MaxAccessCount.HasValue && link.AccessCount >= link.MaxAccessCount.Value) return "ACCESS_LIMIT";
        return null;
    }

    private static string FriendlyDeniedReason(string reason) => reason switch { "REVOKED" => "Este link foi revogado.", "EXPIRED" => "Este link expirou.", "ACCESS_LIMIT" => "O limite de acessos deste link foi atingido.", "INVALID_EXPIRATION" => "Este link está sem expiração válida.", _ => "Link inválido, expirado ou indisponível." };
    private async Task LogAccessAsync(SharedDocumentLinkVm link, bool success, string reason, CancellationToken ct)
    {
        try { await using var conn = await _db.OpenAsync(ct); await conn.ExecuteAsync(new CommandDefinition("insert into ged.secure_document_link_access(tenant_id, secure_link_id, ip_address, user_agent, success, reason) values(@tenantId,@id,@ip,@ua,@success,@reason)", new { tenantId = link.TenantId, id = link.Id, ip = HttpContext.Connection.RemoteIpAddress?.ToString(), ua = Request.Headers.UserAgent.ToString(), success, reason }, cancellationToken: ct)); } catch (Exception ex) { _logger.LogDebug(ex, "Falha ao auditar link seguro."); }
    }
    private async Task<string?> LoadOcrAsync(Guid tenantId, Guid documentId, Guid? versionId, CancellationToken ct) { await using var conn = await _db.OpenAsync(ct); return await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select ocr_text from ged.document_search where tenant_id=@tenantId and document_id=@documentId and (@versionId is null or version_id=@versionId) and nullif(ocr_text,'') is not null order by updated_at desc nulls last limit 1", new { tenantId, documentId, versionId }, cancellationToken: ct)); }
    private static string? BuildSnippet(string text, string term) { var idx = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); if (idx < 0) return null; var start = Math.Max(0, idx - 100); return text.Substring(start, Math.Min(250, text.Length - start)).ReplaceLineEndings(" ").Trim(); }
    private static string Mask(string text) => Regex.Replace(Regex.Replace(text, @"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", "***.***.***-**"), @"\b\d{15}\b", "***************");
    private static string Sha256Token(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public sealed class SharedDocumentLinkVm
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? LoanRequestId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsPermanent { get; set; }
    public int? MaxAccessCount { get; set; }
    public int AccessCount { get; set; }
    public bool AllowPreview { get; set; }
    public bool AllowSmartSearch { get; set; }
    public bool AllowDownload { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
