using System.Globalization;
using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Reports;
using InovaGed.Web.Models.Reports;
using InovaGed.Web.Models.Atlas;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.Relatorios)]
public sealed class ReportsController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentContext _ctx;
    private readonly IReportService _reportSvc;
    private readonly IAuditWriter _audit;

    public ReportsController(
        IDbConnectionFactory db,
        ICurrentContext ctx,
        IReportService reportSvc,
        IAuditWriter audit)
    {
        _db = db;
        _ctx = ctx;
        _reportSvc = reportSvc;
        _audit = audit;
    }

    private Guid TenantId => _ctx.TenantId;
    private Guid UserId => _ctx.UserId;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = new List<ReportCatalogItem>
        {
            new("Documentos cadastrados", "Visão consolidada de volumes, processamento e pendências do acervo.", "documents", "/Ged/Kpi", "Documentos", true),
            new("Documentos sem OCR", "Fila operacional de documentos que ainda não possuem texto pesquisável.", "ocr", "/Ged/Processing?status=pending", "OCR"),
            new("OCR por status", "Acompanhe itens pendentes, em processamento, concluídos e com erro.", "activity", "/Ocr", "OCR"),
            new("Documentos sem classificação", "Itens que precisam de tipologia ou classe documental.", "classification", "/ClassificationDashboard", "Classificação"),
            new("Classificação por classe", "Plano de classificação completo com hierarquia institucional.", "classification", "/Reports/PcdFull", "Classificação", true),
            new("Temporalidade a vencer e vencida", "Prazos de guarda e itens que exigem destinação.", "retention", "/Retention", "Temporalidade"),
            new("Empréstimos e devoluções", "Movimentações físicas, vencimentos e documentos emprestados.", "loan", "/Reports/Loans", "Operação", true),
            new("Auditoria por usuário e período", "Rastreabilidade das ações realizadas no sistema.", "audit", "/Audit", "Governança", true),
            new("Workflows atrasados", "Tramitações que ultrapassaram o prazo definido.", "workflow", "/Operations?onlyOverdue=true", "Operação"),
            new("Acessos negados", "Tentativas bloqueadas para acompanhamento de segurança.", "restricted-access", "/Audit?eventType=ACCESS_DENIED", "Governança", true),
            new("Uploads com falha", "Lotes com arquivos rejeitados ou falhas de processamento.", "upload-cloud", "/Ged/UploadMonitor?status=failed", "Operação"),
            new("Faturamento hospitalar e glosas", "Valores apresentados, aprovados, glosados e recuperados por convênio.", "billing", "/HospitalBilling/Reports", "Financeiro", true),
            new("Acervo físico e ocupação", "Caixas, localizações, capacidade e alertas de lotação.", "archive", "/Physical/Boxes", "Acervo físico", true),
            new("Protocolos e tramitações", "Pendências, responsáveis e prazos de tramitação.", "workflow", "/ProtocoloMelhorias/Relatorios", "Protocolo", true),
            new("Uso do SmartSearch", "Buscas realizadas, perguntas sem resultado e feedbacks negativos.", "search", "/SmartSearch/Statistics", "Inteligência", true),
            new("Validação de assinaturas", "Integridade, cadeia de confiança e resultado das assinaturas.", "certificate-validation", "/Reports/SignatureValidation", "Governança", true)
        };
        await using var conn = await _db.OpenAsync(ct);
        var summary = await conn.QuerySingleAsync<ReportSummary>(
            """
            select count(*) filter (where d.reg_status = 'A') as TotalDocuments,
                   count(*) filter (where d.reg_status = 'A' and exists (
                       select 1 from ged.document_search ds where ds.tenant_id=d.tenant_id and ds.document_id=d.id
                       and nullif(trim(ds.ocr_text),'') is not null)) as WithOcr,
                   count(*) filter (where d.reg_status = 'A' and not exists (
                       select 1 from ged.document_classification dc where dc.tenant_id=d.tenant_id and dc.document_id=d.id
                       and dc.reg_status='A')) as WithoutClassification
              from ged.document d where d.tenant_id=@tenant
            """, new { tenant = TenantId });
        var areas = (await conn.QueryAsync<ReportBreakdownDbRow>(
            """
            select coalesce(f.name, 'Sem setor/pasta') as Label, count(*)::bigint as Total
              from ged.document d left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
             where d.tenant_id=@tenant and d.reg_status='A'
             group by coalesce(f.name, 'Sem setor/pasta') order by count(*) desc, 1 limit 8
            """, new { tenant = TenantId })).ToList();
        var breakdown = areas.Select(x => new ReportBreakdownRow(x.Label, x.Total,
            summary.TotalDocuments == 0 ? 0 : Math.Round(x.Total * 100m / summary.TotalDocuments, 1))).ToList();
        var vm = new ReportsHubVm
        {
            Items = items,
            Metrics =
            [
                new("Documentos", summary.TotalDocuments.ToString("N0"), "ativos no tenant", "neutral", "documents"),
                new("Com OCR", summary.WithOcr.ToString("N0"), summary.TotalDocuments == 0 ? "0% do acervo" : $"{summary.WithOcr * 100m / summary.TotalDocuments:N1}% do acervo", "success", "ocr"),
                new("Sem OCR", (summary.TotalDocuments-summary.WithOcr).ToString("N0"), "requer processamento", "warning", "warning", "/Ged/Processing?status=pending"),
                new("Sem classificação", summary.WithoutClassification.ToString("N0"), "requer tratamento", "warning", "classification", "/ClassificationDashboard")
            ],
            DocumentsByArea = breakdown,
            GeneratedAt = DateTimeOffset.UtcNow
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportDocumentsCsv(string? status, DateTime? from, DateTime? to, CancellationToken ct)
    {
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return BadRequest("O período informado é inválido.");
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<DocumentExportRow>(
            """
            select d.id as Id, coalesce(d.title,'Sem título') as Title, coalesce(d.status,'SEM_STATUS') as Status,
                   coalesce(f.name,'Sem setor/pasta') as Area, d.created_at as CreatedAt
              from ged.document d left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
             where d.tenant_id=@tenant and d.reg_status='A'
               and (@status is null or upper(coalesce(d.status,''))=@status)
               and (@from is null or d.created_at >= @from)
               and (@to is null or d.created_at < @to + interval '1 day')
             order by d.created_at desc, d.id limit 50000
            """, new { tenant = TenantId, status, from = from?.Date, to = to?.Date });
        var list = rows.ToList();
        var csv = new StringBuilder("Documento;Título;Status;Setor/Pasta;Cadastrado em\r\n");
        foreach (var row in list)
            csv.AppendLine(string.Join(';', Csv(row.Id.ToString()), Csv(row.Title), Csv(row.Status), Csv(row.Area), Csv(row.CreatedAt.ToString("O", CultureInfo.InvariantCulture))));
        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into ged.report_export_audit
                (tenant_id,user_id,report_code,export_format,filters_json,row_count,contains_sensitive_data)
            values (@tenant,@user,'DOCUMENTS','CSV',jsonb_build_object('status',@status,'from',@from,'to',@to),@count,false)
            """, new { tenant = TenantId, user = UserId, status, from = from?.Date, to = to?.Date, count = list.Count }, cancellationToken: ct));
        await _audit.WriteAsync(TenantId, UserId, "REPORT_PRINT", "report_export", null,
            "Exportação CSV do relatório de documentos", null, null,
            new { format = "CSV", report = "DOCUMENTS", count = list.Count, status, from, to }, ct);
        var fileName = $"inovaged-documentos-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", fileName);
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private sealed record ReportSummary(long TotalDocuments, long WithOcr, long WithoutClassification);
    private sealed record ReportBreakdownDbRow(string Label, long Total);
    private sealed record DocumentExportRow(Guid Id, string Title, string Status, string Area, DateTimeOffset CreatedAt);

    // =========================================================
    // PLC/PCD — GET /Reports/PcdFull
    // =========================================================
    [HttpGet]
    public async Task<IActionResult> PcdFull(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<TtdRow>(
            SqlClassPlanBase + SqlClassPlanOrder,
            new { tenant = TenantId }
        )).ToList();

        return View("PcdFull", rows);
    }

    // GET /Reports/PcdByClass
    [HttpGet]
    public async Task<IActionResult> PcdByClass(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        // opções para botões rápidos (se sua view usar @model List<string>)
        var examples = (await conn.QueryAsync<string>(
            """
            SELECT DISTINCT code
            FROM ged.classification_plan
            WHERE tenant_id = @tenant
            ORDER BY code
            LIMIT 50
            """,
            new { tenant = TenantId }
        )).ToList();

        return View("PcdByClass", examples);
    }

    // POST /Reports/PcdByClass
    // POST /Reports/PcdByClass
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PcdByClass(string? classCode, string? code, CancellationToken ct)
    {
        // aceita tanto "classCode" quanto "code"
        classCode = (classCode ?? code ?? "").Trim();

        if (string.IsNullOrWhiteSpace(classCode))
        {
            TempData["err"] = "Informe um código (formato: PCD-001).";
            return RedirectToAction(nameof(PcdByClass));
        }

        await using var conn = await _db.OpenAsync(ct);

        var sql =
            SqlClassPlanBase +
            """
        AND (code = @code OR code LIKE (@code || '.%'))
        """ +
            SqlClassPlanOrder;

        var rows = (await conn.QueryAsync<TtdRow>(
            sql,
            new { tenant = TenantId, code = classCode }
        )).ToList();

        if (rows.Count == 0)
        {
            TempData["err"] = $"Nenhuma classe encontrada para: {classCode}";
            return RedirectToAction(nameof(PcdByClass));
        }

        return View("PcdFull", rows);
    }

    // =========================================================
    // TTD — GET /Reports/TtdFull
    // =========================================================
    [HttpGet]
    public async Task<IActionResult> TtdFull(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        var rows = (await conn.QueryAsync<TtdRow>(
            SqlClassPlanBase + SqlClassPlanOrder,
            new { tenant = TenantId }
        )).ToList();

        return View("TtdFull", rows);
    }

    // GET /Reports/TtdByClass
    [HttpGet]
    public async Task<IActionResult> TtdByClass(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        // opções para botões rápidos (conforme @model List<string>)
        var examples = (await conn.QueryAsync<string>(
            """
            SELECT DISTINCT code
            FROM ged.classification_plan
            WHERE tenant_id = @tenant
            ORDER BY code
            LIMIT 50
            """,
            new { tenant = TenantId }
        )).ToList();

        return View("TtdByClass", examples);
    }

    // POST /Reports/TtdByClass
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TtdByClass(string classCode, CancellationToken ct)
    {
        classCode = (classCode ?? "").Trim();

        if (string.IsNullOrWhiteSpace(classCode))
        {
            TempData["err"] = "Informe um código (formato: TTD-001).";
            return RedirectToAction(nameof(TtdByClass));
        }

        using var conn = await _db.OpenAsync(ct);

        var sql =
            SqlClassPlanBase +
            """
            AND (code = @code OR code LIKE (@code || '.%'))
            """ +
            SqlClassPlanOrder;

        var rows = (await conn.QueryAsync<TtdRow>(
            sql,
            new { tenant = TenantId, code = classCode }
        )).ToList();

        if (rows.Count == 0)
            TempData["err"] = $"Nenhuma classe encontrada para: {classCode}";

        return View("TtdFull", rows);
    }

    // =========================================================
    // Empréstimos — GET /Reports/Loans
    // =========================================================
    [HttpGet]
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
    // Item 21 operacional
    // =========================================================
    [HttpGet]
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
    [HttpGet]
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
    [HttpGet]
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
