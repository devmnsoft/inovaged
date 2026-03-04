using Dapper;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;
using InovaGed.Web.ViewModels;

namespace InovaGed.Web.Controllers;

public class SignatureController : GedControllerBase
{
    public SignatureController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    // GET /Signature
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        using var db = await OpenAsync();

        var docs = await db.QueryAsync<SignatureIndexVm.DocumentRow>(
            """
        select
            d.id as "Id",
            d.code as "Code",
            d.title as "Title"
        from ged.document d
        where d.tenant_id=@tid
        order by d.created_at desc nulls last
        limit 20;
        """,
            new { tid = TenantId }
        );

        var batches = await db.QueryAsync<SignatureIndexVm.BatchRow>(
            """
        select
            b.id as "Id",
            ('LOTE-' || lpad(b.batch_no::text, 6, '0')) as "Code",
            b.status::text as "Status"
        from ged.batch b
        where b.tenant_id=@tid
          and b.reg_status='A'
        order by b.created_at desc nulls last, b.batch_no desc
        limit 20;
        """,
            new { tid = TenantId }
        );

        var vm = new SignatureIndexVm
        {
            Docs = docs.ToList(),
            Batches = batches.ToList()
        };

        ViewData["Title"] = "Assinar (Documento / Lote)";
        ViewData["Subtitle"] = "ICP – assinatura PoC (8,19,20,21)";

        return View(vm);
    }

    // GET /Signature/SignDocument?docId=...
    [HttpGet]
    public async Task<IActionResult> SignDocument(Guid docId, CancellationToken ct)
    {
        using var db = await OpenAsync();

        var doc = await db.QueryFirstOrDefaultAsync(
            """
            select id, code, title, status
            from ged.document
            where tenant_id=@tid and id=@docId;
            """,
            new { tid = TenantId, docId }
        );

        if (doc == null) return NotFound("Documento não encontrado.");

        return View(doc);
    }

    // POST /Signature/SignDocument
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignDocument(Guid docId, string userCpf, string certCpf, string status = "UNKNOWN", string? details = null, CancellationToken ct = default)
    {
        using var db = await OpenAsync();

        var u = NormalizeCpf(userCpf);
        var c = NormalizeCpf(certCpf);

        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(c))
            return BadRequest("CPF inválido.");

        // Item 8: CPF usuário = CPF do certificado
        if (!string.Equals(u, c, StringComparison.Ordinal))
            return BadRequest("CPF do usuário diferente do CPF do certificado. Assinatura bloqueada (PoC item 8).");

        await db.ExecuteAsync(
            new CommandDefinition(
                """
                insert into ged.document_signature
                (id, tenant_id, document_id, signed_by, signed_by_name, cpf, signing_time, status, status_details, reg_date, reg_status)
                values
                (gen_random_uuid(), @tid, @docId, @uid, @uname, @cpf, now(), @status::ged.signature_status, @details, now(), 'A');
                """,
                new
                {
                    tid = TenantId,
                    docId,
                    uid = UserId,
                    uname = UserNameSafe,
                    cpf = u,
                    status,
                    details
                },
                cancellationToken: ct
            )
        );

        return RedirectToAction(nameof(SignDocument), new { docId });
    }

    // GET /Signature/SignBatch?batchId=...
    [HttpGet]
    public async Task<IActionResult> SignBatch(Guid batchId, CancellationToken ct)
    {
        using var db = await OpenAsync();

        var batch = await db.QueryFirstOrDefaultAsync(
            """
            select id, code, status
            from ged.batch
            where tenant_id=@tid and id=@batchId;
            """,
            new { tid = TenantId, batchId }
        );

        if (batch == null) return NotFound("Lote não encontrado.");

        var docs = await db.QueryAsync(
            """
            select d.id, d.code, d.title, d.status
            from ged.batch_item bi
            join ged.document d
              on d.id = bi.document_id
             and d.tenant_id = bi.tenant_id
            where bi.tenant_id=@tid
              and bi.batch_id=@batchId
            order by bi.reg_date;
            """,
            new { tid = TenantId, batchId }
        );

        // (se quiser deixar 100% tipado, eu te passo o BatchVm também)
        return View(new { batch, docs });
    }

    // POST /Signature/SignBatch
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignBatch(Guid batchId, string userCpf, string certCpf, string status = "UNKNOWN", string? details = null, CancellationToken ct = default)
    {
        using var db = await OpenAsync();

        var u = NormalizeCpf(userCpf);
        var c = NormalizeCpf(certCpf);

        if (!string.Equals(u, c, StringComparison.Ordinal))
            return BadRequest("CPF do usuário diferente do CPF do certificado. Assinatura em lote bloqueada.");

        var docIds = await db.QueryAsync<Guid>(
            """
            select document_id
            from ged.batch_item
            where tenant_id=@tid and batch_id=@batchId;
            """,
            new { tid = TenantId, batchId }
        );

        foreach (var docId in docIds)
        {
            await db.ExecuteAsync(
                new CommandDefinition(
                    """
                    insert into ged.document_signature
                    (id, tenant_id, document_id, signed_by, signed_by_name, cpf, signing_time, status, status_details, reg_date, reg_status)
                    values
                    (gen_random_uuid(), @tid, @docId, @uid, @uname, @cpf, now(), @status::ged.signature_status, @details, now(), 'A');
                    """,
                    new
                    {
                        tid = TenantId,
                        docId,
                        uid = UserId,
                        uname = UserNameSafe,
                        cpf = u,
                        status,
                        details
                    },
                    cancellationToken: ct
                )
            );
        }

        return RedirectToAction(nameof(SignBatch), new { batchId });
    }

    private static string NormalizeCpf(string cpf)
        => new string((cpf ?? "").Where(char.IsDigit).ToArray());
}