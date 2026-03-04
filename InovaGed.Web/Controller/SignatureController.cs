using Dapper;
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

    public SignatureController(IDbConnectionFactory db, ICurrentContext ctx)
    {
        _db = db;
        _ctx = ctx;
    }

    private Guid TenantId => _ctx.TenantId;
    private Guid UserId => _ctx.UserId;

    [HttpGet("Document/{id}")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var doc = await conn.QuerySingleAsync(@"
        SELECT
            d.id,
            d.title,
            s.signature_status,
            s.signed_at,
            s.cpf_certificado
        FROM ged.document d
        LEFT JOIN ged.document_signature s
        ON s.document_id = d.id
        WHERE d.id = @id
        ", new { id });

        return View(doc);
    }

    [HttpPost("Sign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(Guid documentId, string cpfCertificado, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var userCpf = await conn.ExecuteScalarAsync<string>(@"
        SELECT cpf FROM core.user WHERE id=@userId
        ", new { userId = UserId });

        if (userCpf != cpfCertificado)
        {
            TempData["Err"] = "CPF do certificado não corresponde ao usuário.";
            return RedirectToAction("Document", new { id = documentId });
        }

        await conn.ExecuteAsync(@"
        INSERT INTO ged.document_signature
        (id,document_id,user_id,cpf_certificado,signature_status,signed_at)
        VALUES
        (gen_random_uuid(),@documentId,@userId,@cpf,'VALIDA',now())
        ",
        new
        {
            documentId,
            userId = UserId,
            cpf = cpfCertificado
        });

        TempData["Ok"] = "Documento assinado.";

        return RedirectToAction("Document", new { id = documentId });
    }
}