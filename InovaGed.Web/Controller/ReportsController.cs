using Dapper;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Reports;
using InovaGed.Web.Models.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ReportsController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentContext _ctx;
    private readonly IReportService _reportSvc;

    public ReportsController(
        IDbConnectionFactory db,
        ICurrentContext ctx,
        IReportService reportSvc)
    {
        _db = db;
        _ctx = ctx;
        _reportSvc = reportSvc;
    }

    private Guid TenantId => _ctx.TenantId;
    private Guid UserId => _ctx.UserId;

    // =========================================================
    // PLC/PCD — GET /Reports/PcdFull
    // =========================================================
    public async Task<IActionResult> PcdFull(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<TtdRow>(
            SqlClassPlanBase + SqlClassPlanOrder,
            new { tenant = TenantId }
        )).ToList();

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

        // NOTE: começa com quebra de linha para NÃO colar em @tenant
        var sql =
            SqlClassPlanBase +
            """
            AND (code = @code OR code LIKE (@code || '.%'))
            """ +
            SqlClassPlanOrder;

        var rows = (await conn.QueryAsync<TtdRow>(
            sql,
            new { tenant = TenantId, code }
        )).ToList();

        return View("PcdFull", rows);
    }

    // =========================================================
    // TTD — GET /Reports/TtdFull
    // =========================================================
    public async Task<IActionResult> TtdFull(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<TtdRow>(
            SqlClassPlanBase + SqlClassPlanOrder,
            new { tenant = TenantId }
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

        // NOTE: começa com quebra de linha para NÃO colar em @tenant
        var sql =
            SqlClassPlanBase +
            """
            AND (code = @code OR code LIKE (@code || '.%'))
            """ +
            SqlClassPlanOrder;

        var rows = (await conn.QueryAsync<TtdRow>(
            sql,
            new { tenant = TenantId, code }
        )).ToList();

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
            SELECT protocol_no    AS ProtocolNo,
                   requester_name AS RequesterName,
                   requested_at   AS RequestedAt,
                   due_at         AS DueAt,
                   status         AS Status,
                   document_code  AS DocumentCode,
                   document_title AS DocumentTitle
            FROM ged.vw_loan_report
            WHERE tenant_id = @tenant
            ORDER BY requested_at DESC
            """,
            new { tenant = TenantId }
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
            SqlSig,
            new { tenant = TenantId }
        )).ToList();

        return View("SignatureValidation", rows);
    }

    // =========================================================
    // ITEM 26 — Tela de seleção de documentos assinados
    // GET /Reports/SignedSetPrint
    // =========================================================
    public async Task<IActionResult> SignedSetPrint(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var docs = (await conn.QueryAsync<SignedDocRow>(
            """
            SELECT
                d.id              AS DocumentId,
                d.code            AS DocumentCode,
                d.title           AS DocumentTitle,
                s.signed_by_name  AS SignerName,
                s.cpf             AS Cpf,
                s.signing_time    AS SigningTime,
                s.status::text    AS SigStatus,
                s.status_details  AS SigDetails
            FROM ged.document_signature s
            JOIN ged.document d
              ON d.tenant_id = s.tenant_id
             AND d.id        = s.document_id
            WHERE s.tenant_id  = @tenant
              AND s.reg_status = 'A'
            ORDER BY d.code, s.signing_time DESC NULLS LAST
            """,
            new { tenant = TenantId }
        )).ToList();

        return View("SignedSetPrint", new SignedSetSelectVM(docs));
    }

    // =========================================================
    // ITEM 26 — Geração do relatório (POST)
    // =========================================================
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SignedSetPrint([FromForm] List<Guid> documentIds, CancellationToken ct)
    {
        if (documentIds == null || documentIds.Count == 0)
        {
            TempData["Err"] = "Selecione ao menos um documento para gerar o relatório.";
            return RedirectToAction(nameof(SignedSetPrint));
        }

        var vm = new ReportRunCreateVM
        {
            ReportType = "SIGNED_SET_PRINT",
            DocumentIds = documentIds.Distinct().ToList(),
            Notes = $"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm} pelo usuário {UserId}"
        };

        var result = await _reportSvc.CreateReportRunWithSignatureSnapshotAsync(
            TenantId, UserId, vm, ct);

        if (!result.Success)
        {
            TempData["Err"] = result.Error?.Message ?? "Falha ao gerar relatório.";
            return RedirectToAction(nameof(SignedSetPrint));
        }

        return RedirectToAction(nameof(SignedSetPrintView), new { runId = result.Value });
    }

    // =========================================================
    // ITEM 26 — View de impressão
    // =========================================================
    public async Task<IActionResult> SignedSetPrintView(Guid runId, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var run = await conn.QuerySingleOrDefaultAsync<(Guid Id, DateTime GeneratedAt)>(
            """
            SELECT id AS Id, generated_at AS GeneratedAt
            FROM ged.report_run
            WHERE id = @runId AND tenant_id = @tenant AND reg_status = 'A'
            """,
            new { runId, tenant = TenantId }
        );

        if (run == default)
            return NotFound("Relatório não encontrado.");

        var items = (await conn.QueryAsync<SignedSetPrintItem>(
            """
            SELECT
                ROW_NUMBER() OVER (ORDER BY d.code, s.validated_at) AS SeqNo,
                d.id                AS DocumentId,
                d.code              AS DocumentCode,
                d.title             AS DocumentTitle,
                ds.signed_by_name   AS SignerName,
                ds.cpf              AS Cpf,
                ds.signing_time     AS SigningTime,
                s.signature_status::text AS SigStatus,
                s.status_details    AS SigDetails,
                s.validated_at      AS ValidatedAt
            FROM ged.report_run_signature s
            JOIN ged.document d
              ON d.tenant_id = s.tenant_id
             AND d.id        = s.document_id
            LEFT JOIN ged.document_signature ds
              ON ds.id = s.signature_id
            WHERE s.report_run_id = @runId
              AND s.tenant_id     = @tenant
              AND s.reg_status    = 'A'
            ORDER BY d.code, s.validated_at
            """,
            new { runId, tenant = TenantId }
        )).ToList();

        var printVm = new SignedSetPrintVm(
            RunId: runId,
            GeneratedAt: run.GeneratedAt,
            Items: items);

        return View("SignedSetPrintView", printVm);
    }

    // ---------------------------------------------------------
    // SQL helpers
    // ---------------------------------------------------------
    private const string SqlClassPlanBase =
        """
        SELECT id,
               code                        AS ClassCode,
               name                        AS ClassName,
               0                           AS CurrentDays,
               0                           AS IntermediateDays,
               retention_active_months     AS ActiveMonths,
               retention_archive_months    AS ArchiveMonths,
               final_destination::text     AS FinalDestination,
               retention_start_event::text AS StartEvent,
               retention_notes             AS Notes
        FROM ged.classification_plan
        WHERE tenant_id = @tenant

        """; // <-- mantém esta linha em branco no final (importante)

    private const string SqlClassPlanOrder =
        """
        ORDER BY code
        """;

    private const string SqlSig =
        """
        SELECT d.code           AS DocumentCode,
               d.title          AS DocumentTitle,
               s.status::text   AS Status,
               s.signing_time   AS SigningTime,
               s.signed_by_name AS SignedByName,
               s.cpf            AS Cpf,
               s.status_details AS Details
        FROM ged.document_signature s
        JOIN ged.document d
          ON d.tenant_id = s.tenant_id
         AND d.id        = s.document_id
        WHERE s.tenant_id  = @tenant
          AND s.reg_status = 'A'
        ORDER BY s.signing_time DESC NULLS LAST
        """;
}