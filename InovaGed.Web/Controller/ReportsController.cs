using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ReportsController : Controller
{
    private readonly IDbConnectionFactory _db;
    public ReportsController(IDbConnectionFactory db) => _db = db;

    // =========================================================
    // PLC/PCD — GET /Reports/PcdFull
    // =========================================================
    public async Task<IActionResult> PcdFull(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<TtdRow>(SqlTtd, new { tenant = TenantId() })).ToList();
        return View("PcdFull", rows);
    }

    public IActionResult PcdByClass() => View("PcdByClass");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PcdByClass(string code, CancellationToken ct)
    {
        code = (code ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            TempData["err"] = "Informe um código (ex.: PCD-001).";
            return RedirectToAction(nameof(PcdByClass));
        }
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<TtdRow>(
            SqlTtd + " and (code = @code or code like (@code || '.%'))",
            new { tenant = TenantId(), code })).ToList();
        return View("PcdFull", rows);
    }

    // =========================================================
    // TTD — GET /Reports/TtdFull
    // =========================================================
    public async Task<IActionResult> TtdFull(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<TtdRow>(SqlTtd, new { tenant = TenantId() })).ToList();
        return View("TtdFull", rows);
    }

    public IActionResult TtdByClass() => View("TtdByClass");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TtdByClass(string code, CancellationToken ct)
    {
        code = (code ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            TempData["err"] = "Informe um código (ex.: PCD-001).";
            return RedirectToAction(nameof(TtdByClass));
        }
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<TtdRow>(
            SqlTtd + " and (code = @code or code like (@code || '.%'))",
            new { tenant = TenantId(), code })).ToList();
        return View("TtdFull", rows);
    }

    // =========================================================
    // Empréstimos — GET /Reports/Loans
    // =========================================================
    public async Task<IActionResult> Loans(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<LoanReportRow>(
            """
            select protocol_no    as ProtocolNo,
                   requester_name as RequesterName,
                   requested_at   as RequestedAt,
                   due_at         as DueAt,
                   status         as Status,
                   document_code  as DocumentCode,
                   document_title as DocumentTitle
            from ged.vw_loan_report
            where tenant_id = @tenant
            order by requested_at desc
            """, new { tenant = TenantId() }
        )).ToList();
        return View("Loans", rows);
    }

    // =========================================================
    // Validação de Assinaturas — GET /Reports/SignatureValidation
    // Item 21 PoC
    // =========================================================
    public async Task<IActionResult> SignatureValidation(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<SignatureValidationRow>(
            SqlSig, new { tenant = TenantId() }
        )).ToList();
        return View("SignatureValidation", rows);
    }

    // =========================================================
    // Impressão conjunto assinado — GET /Reports/SignedSetPrint
    // Item 26 PoC
    // =========================================================
    public async Task<IActionResult> SignedSetPrint(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<SignatureValidationRow>(
            SqlSig + " and s.status = 'VALID'", new { tenant = TenantId() }
        )).ToList();

        var vm = new SignedSetPrintVm(
            RunId: Guid.NewGuid(),
            GeneratedAt: DateTime.UtcNow,
            Items: rows
        );

        return View("SignedSetPrint", vm);
    }

    // ---------------------------------------------------------
    // SQL helpers
    // ---------------------------------------------------------
    private const string SqlTtd =
        """
        select id,
               code                        as ClassCode,
               name                        as ClassName,
               0                           as CurrentDays,
               0                           as IntermediateDays,
               retention_active_months     as ActiveMonths,
               retention_archive_months    as ArchiveMonths,
               final_destination::text     as FinalDestination,
               retention_start_event::text as StartEvent,
               retention_notes             as Notes
        from ged.classification_plan
        where tenant_id = @tenant
        order by code
        """;

    private const string SqlSig =
        """
        select d.code           as DocumentCode,
               d.title          as DocumentTitle,
               s.status::text   as Status,
               s.signing_time   as SigningTime,
               s.signed_by_name as SignedByName,
               s.cpf            as Cpf,
               s.status_details as Details
        from ged.document_signature s
        join ged.document d
          on d.tenant_id = s.tenant_id
         and d.id        = s.document_id
        where s.tenant_id  = @tenant
          and s.reg_status = 'A'
        order by s.signing_time desc nulls last
        """;

    private Guid TenantId()
        => Guid.Parse("00000000-0000-0000-0000-000000000001");
}