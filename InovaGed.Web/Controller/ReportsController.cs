using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ReportsController : Controller
{
    private readonly IDbConnectionFactory _db;
    public ReportsController(IDbConnectionFactory db) => _db = db;

    // =========================================================
    // PLC/PCD - Relatório completo
    // GET /Reports/PcdFull
    // =========================================================
    public async Task<IActionResult> PcdFull(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<PcdRow>(
            """
            select
              id,
              code,
              name as Title,
              parent_id as ParentId,
              description as Description
            from ged.classification_plan
            where tenant_id = @tenant
            order by code
            """, new { tenant = TenantId() }
        )).ToList();

        return View("PcdFull", rows);
    }

    // GET /Reports/PcdByClass
    public IActionResult PcdByClass() => View("PcdByClass");

    // POST /Reports/PcdByClass
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

        var rows = (await conn.QueryAsync<PcdRow>(
            """
            select
              id,
              code,
              name as Title,
              parent_id as ParentId,
              description as Description
            from ged.classification_plan
            where tenant_id = @tenant
              and (code = @code or code like (@code || '.%'))
            order by code
            """,
            new { tenant = TenantId(), code }
        )).ToList();

        return View("PcdFull", rows);
    }

    // =========================================================
    // TTD - Relatório (usa campos reais da classification_plan)
    // GET /Reports/TtdFull
    // =========================================================
    public async Task<IActionResult> TtdFull(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<TtdRow>(
            """
            select
              id,
              code as ClassCode,
              name as ClassName,
              retention_active_months as ActiveMonths,
              retention_archive_months as ArchiveMonths,
              final_destination as FinalDestination,
              retention_start_event as StartEvent,
              retention_notes as Notes
            from ged.classification_plan
            where tenant_id = @tenant
            order by code
            """, new { tenant = TenantId() }
        )).ToList();

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
            """
            select
              id,
              code as ClassCode,
              name as ClassName,
              retention_active_months as ActiveMonths,
              retention_archive_months as ArchiveMonths,
              final_destination as FinalDestination,
              retention_start_event as StartEvent,
              retention_notes as Notes
            from ged.classification_plan
            where tenant_id = @tenant
              and (code = @code or code like (@code || '.%'))
            order by code
            """, new { tenant = TenantId(), code }
        )).ToList();

        return View("TtdFull", rows);
    }

    // =========================================================
    // Empréstimos - Relatório
    // GET /Reports/Loans  (está no seu menu)
    // =========================================================
    public async Task<IActionResult> Loans(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<LoanReportRow>(
            """
            select
              protocol_no as ProtocolNo,
              requester_name as RequesterName,
              requested_at as RequestedAt,
              due_at as DueAt,
              status as Status,
              document_code as DocumentCode,
              document_title as DocumentTitle
            from ged.vw_loan_report
            where tenant_id = @tenant
            order by requested_at desc
            """, new { tenant = TenantId() }
        )).ToList();

        return View("Loans", rows);
    }

    // =========================================================
    // Assinaturas - Validação (está no seu menu)
    // GET /Reports/SignatureValidation
    // =========================================================
    public async Task<IActionResult> SignatureValidation(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<SignatureRow>(
            """
            select
              d.code as DocumentCode,
              d.title as DocumentTitle,
              s.status::text as Status,
              s.signing_time as SigningTime,
              s.signed_by_name as SignedByName,
              s.cpf as Cpf,
              s.status_details as Details
            from ged.document_signature s
            join ged.document d on d.id = s.document_id
            where s.tenant_id = @tenant
            order by s.signing_time desc
            """, new { tenant = TenantId() }
        )).ToList();

        return View("SignatureValidation", rows);
    }

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------
    private Guid TenantId()
    {
        // na PoC você está usando tenant fixo.
        // Se você já tem tenant no Claims, substitua aqui.
        return Guid.Parse("00000000-0000-0000-0000-000000000001");
    }

    // ---------------------------------------------------------
    // ViewModels
    // ---------------------------------------------------------
    public sealed record PcdRow(Guid Id, string Code, string Title, Guid? ParentId, string? Description);

    public sealed record TtdRow(
       Guid Id,
       string ClassCode,
       string ClassName,
        int CurrentDays,
        int IntermediateDays,
       int? ActiveMonths,
       int? ArchiveMonths,
       string? FinalDestination,
       string? StartEvent,
       string? Notes);

    public sealed record LoanReportRow(
        long ProtocolNo,
        string RequesterName,
        DateTime RequestedAt,
        DateTime DueAt,
        string Status,
        string DocumentCode,
        string DocumentTitle);

    public sealed record SignatureRow(
        string DocumentCode,
        string DocumentTitle,
        string Status,
        DateTime SigningTime,
        string SignedByName,
        string Cpf,
        string? Details);
}