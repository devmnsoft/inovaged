using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class SignatureController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentContext _ctx;
    private readonly IAuditWriter _audit;
    private readonly ILogger<SignatureController> _logger;

    public SignatureController(
        IDbConnectionFactory db,
        ICurrentContext ctx,
        IAuditWriter audit,
        ILogger<SignatureController> logger)
    {
        _db = db;
        _ctx = ctx;
        _audit = audit;
        _logger = logger;
    }

    private Guid TenantId => _ctx.TenantId;
    private Guid UserId => _ctx.UserId;
    private string UserName => _ctx.UserDisplay ?? _ctx.UserEmail ?? "Usuário";

    // =========================================================
    // GET /Signature  — lista documentos
    // =========================================================
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            var rows = (await conn.QueryAsync<PendingDocRow>("""
                SELECT
                    d.id               AS Id,
                    d.code             AS Code,
                    d.title            AS Title,
                    d.status::text     AS DocStatus,
                    d.created_at       AS CreatedAt,
                    s.status::text     AS SignatureStatus,
                    s.signing_time     AS SigningTime,
                    s.signed_by_name   AS SignedByName
                FROM ged.document d
                LEFT JOIN ged.document_signature s
                    ON s.tenant_id = d.tenant_id
                   AND s.document_id = d.id
                   AND s.reg_status = 'A'
                WHERE d.tenant_id = @tenantId
                  AND d.status::text NOT IN ('ARCHIVED', 'DELETED')
                  AND (
                        @q::text IS NULL
                        OR d.title ILIKE '%' || @q || '%'
                        OR d.code  ILIKE '%' || @q || '%'
                  )
                ORDER BY d.created_at DESC
                LIMIT 200;
                """,
                new { tenantId = TenantId, q = string.IsNullOrWhiteSpace(q) ? null : q }
            )).ToList();

            ViewBag.Q = q;
            return View(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature.Index failed");
            TempData["Err"] = "Erro ao carregar documentos.";
            return View(new List<PendingDocRow>());
        }
    }

    // =========================================================
    // GET /Signature/Document/{id}  — formulário de assinatura
    // =========================================================
    [HttpGet("Document/{id:guid}")]
    public async Task<IActionResult> SignDocument(Guid id, CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            var vm = await conn.QuerySingleOrDefaultAsync<SignDocumentVM>("""
                SELECT
                    d.id               AS Id,
                    d.code             AS Code,
                    d.title            AS Title,
                    d.status::text     AS DocStatus,
                    s.status::text     AS SignatureStatus,
                    s.signing_time     AS SigningTime,
                    s.signed_by_name   AS SignedByName,
                    s.cpf              AS Cpf,
                    s.status_details   AS StatusDetails
                FROM ged.document d
                LEFT JOIN ged.document_signature s
                    ON s.tenant_id = d.tenant_id
                   AND s.document_id = d.id
                   AND s.reg_status = 'A'
                WHERE d.tenant_id = @tenantId
                  AND d.id = @id;
                """,
                new { tenantId = TenantId, id });

            if (vm is null) return NotFound();
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature.SignDocument GET failed. Doc={Id}", id);
            TempData["Err"] = "Erro ao carregar documento.";
            return RedirectToAction(nameof(Index));
        }
    }

    // =========================================================
    // POST /Signature/Document/{id}  — grava assinatura
    // =========================================================
    [HttpPost("Document/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignDocument(Guid id, string cpf, string? notes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cpf))
        {
            TempData["Err"] = "CPF do certificado é obrigatório.";
            return RedirectToAction(nameof(SignDocument), new { id });
        }

        try
        {
            // Gera hash SHA-256 interno (PoC — sem certificado real)
            var payload = $"doc:{id}|tenant:{TenantId}|user:{UserId}|cpf:{cpf.Trim()}|ts:{DateTime.UtcNow:O}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

            var details = string.IsNullOrWhiteSpace(notes)
                ? $"Assinatura interna (PoC) • hash:{hash[..16]}…"
                : $"{notes.Trim()} • hash:{hash[..16]}…";

            using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // Invalida assinaturas anteriores do mesmo documento (sem UNIQUE constraint, usamos reg_status)
            await conn.ExecuteAsync("""
                UPDATE ged.document_signature
                SET reg_status = 'I'
                WHERE tenant_id = @tenantId
                  AND document_id = @docId
                  AND reg_status = 'A';
                """,
                new { tenantId = TenantId, docId = id }, tx);

            // Insere nova assinatura com colunas reais da tabela
            await conn.ExecuteAsync("""
                INSERT INTO ged.document_signature
                    (id, tenant_id, document_id, signed_by, signed_by_name,
                     cpf, cert_subject, cert_serial,
                     signing_time, status, status_details, reg_date, reg_status)
                VALUES
                    (gen_random_uuid(), @tenantId, @docId, @userId, @signedByName,
                     @cpf, @certSubject, @certSerial,
                     now(), 'VALID'::ged.signature_status, @details, now(), 'A');
                """,
                new
                {
                    tenantId = TenantId,
                    docId = id,
                    userId = UserId,
                    signedByName = UserName,
                    cpf = cpf.Trim(),
                    certSubject = $"CN={UserName};CPF={cpf.Trim()}",
                    certSerial = hash[..16].ToUpperInvariant(),
                    details
                }, tx);

            tx.Commit();

            await _audit.WriteAsync(
                TenantId, UserId, "UPDATE", "document_signature", id,
                $"Documento assinado por {UserName} (CPF {cpf.Trim()})",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                new { documentId = id, cpf = cpf.Trim(), hash = hash[..16] },
                ct);

            TempData["Ok"] = "Documento assinado com sucesso.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature.SignDocument POST failed. Doc={Id}", id);
            TempData["Err"] = "Erro ao registrar assinatura.";
            return RedirectToAction(nameof(SignDocument), new { id });
        }
    }

    // =========================================================
    // GET /Signature/Batch  — seletor de lote
    // =========================================================
    [HttpGet("Batch")]
    public async Task<IActionResult> SignBatch(CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            var docs = (await conn.QueryAsync<PendingDocRow>("""
                SELECT
                    d.id               AS Id,
                    d.code             AS Code,
                    d.title            AS Title,
                    d.status::text     AS DocStatus,
                    d.created_at       AS CreatedAt,
                    s.status::text     AS SignatureStatus,
                    s.signing_time     AS SigningTime,
                    s.signed_by_name   AS SignedByName
                FROM ged.document d
                LEFT JOIN ged.document_signature s
                    ON s.tenant_id = d.tenant_id
                   AND s.document_id = d.id
                   AND s.reg_status = 'A'
                WHERE d.tenant_id = @tenantId
                  AND d.status::text NOT IN ('ARCHIVED', 'DELETED')
                ORDER BY d.code
                LIMIT 500;
                """, new { tenantId = TenantId }
            )).ToList();

            return View(docs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature.SignBatch GET failed");
            TempData["Err"] = "Erro ao carregar documentos.";
            return View(new List<PendingDocRow>());
        }
    }

    // =========================================================
    // POST /Signature/Batch  — assina lote
    // =========================================================
    [HttpPost("Batch")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignBatch(
        List<Guid> documentIds, string cpf, string? notes, CancellationToken ct)
    {
        if (documentIds is null || documentIds.Count == 0)
        {
            TempData["Err"] = "Selecione ao menos um documento.";
            return RedirectToAction(nameof(SignBatch));
        }

        if (string.IsNullOrWhiteSpace(cpf))
        {
            TempData["Err"] = "CPF do certificado é obrigatório.";
            return RedirectToAction(nameof(SignBatch));
        }

        var ids = documentIds.Distinct().ToArray();

        try
        {
            using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // Invalida assinaturas anteriores do lote
            await conn.ExecuteAsync("""
                UPDATE ged.document_signature
                SET reg_status = 'I'
                WHERE tenant_id = @tenantId
                  AND document_id = ANY(@ids)
                  AND reg_status = 'A';
                """,
                new { tenantId = TenantId, ids }, tx);

            // Insere uma assinatura por documento do lote
            foreach (var docId in ids)
            {
                var payload = $"doc:{docId}|tenant:{TenantId}|user:{UserId}|cpf:{cpf.Trim()}|ts:{DateTime.UtcNow:O}";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

                var details = string.IsNullOrWhiteSpace(notes)
                    ? $"Assinatura em lote (PoC) • hash:{hash[..16]}…"
                    : $"{notes.Trim()} • hash:{hash[..16]}…";

                await conn.ExecuteAsync("""
                    INSERT INTO ged.document_signature
                        (id, tenant_id, document_id, signed_by, signed_by_name,
                         cpf, cert_subject, cert_serial,
                         signing_time, status, status_details, reg_date, reg_status)
                    VALUES
                        (gen_random_uuid(), @tenantId, @docId, @userId, @signedByName,
                         @cpf, @certSubject, @certSerial,
                         now(), 'VALID'::ged.signature_status, @details, now(), 'A');
                    """,
                    new
                    {
                        tenantId = TenantId,
                        docId,
                        userId = UserId,
                        signedByName = UserName,
                        cpf = cpf.Trim(),
                        certSubject = $"CN={UserName};CPF={cpf.Trim()}",
                        certSerial = hash[..16].ToUpperInvariant(),
                        details
                    }, tx);
            }

            tx.Commit();

            await _audit.WriteAsync(
                TenantId, UserId, "UPDATE", "document_signature", null,
                $"Lote de {ids.Length} documento(s) assinado por {UserName} (CPF {cpf.Trim()})",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                new { count = ids.Length, cpf = cpf.Trim() },
                ct);

            TempData["Ok"] = $"{ids.Length} documento(s) assinado(s) com sucesso.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature.SignBatch POST failed");
            TempData["Err"] = "Erro ao registrar assinaturas em lote.";
            return RedirectToAction(nameof(SignBatch));
        }
    }

    // =========================================================
    // ViewModels
    // =========================================================
    public sealed record PendingDocRow(
        Guid Id,
        string Code,
        string Title,
        string DocStatus,
        DateTime CreatedAt,
        string? SignatureStatus,
        DateTime? SigningTime,
        string? SignedByName);

    public sealed record SignDocumentVM(
        Guid Id,
        string Code,
        string Title,
        string DocStatus,
        string? SignatureStatus,
        DateTime? SigningTime,
        string? SignedByName,
        string? Cpf,
        string? StatusDetails);
}