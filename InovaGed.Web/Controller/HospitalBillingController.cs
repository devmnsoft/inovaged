using InovaGed.Application.HospitalBilling;
using InovaGed.Application.Identity;
using InovaGed.Application.Audit;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.HospitalDocumentsAccess)]
[Route("HospitalBilling")]
public sealed class HospitalBillingController(ICurrentUser user, IHospitalBillingQueries queries, IAuditWriter audit) : Controller
{
    [HttpGet("")][HttpGet("Dashboard")]
    public async Task<IActionResult> Index(string? insurer, string? competence, string? status, bool? hasDenial, string? term, string? unit, string? patient, string? documentType, decimal? minimumAmount, decimal? maximumAmount, bool? ocrPending, bool? hasDivergence, CancellationToken ct)
    { var filter = new HospitalBillingFilter(insurer, competence, status, hasDenial, term, unit, patient, documentType, minimumAmount, maximumAmount, ocrPending, hasDivergence); ViewBag.Filter = filter; ViewBag.Mode ??= "dashboard"; return View(await queries.DashboardAsync(user.TenantId, filter, ct)); }
    [HttpGet("Documents")]
    public Task<IActionResult> Documents(string? insurer, string? competence, string? status, bool? hasDenial, string? term, CancellationToken ct)
    { ViewBag.Mode = "documents"; return Index(insurer, competence, status, hasDenial, term, null, null, null, null, null, null, null, ct); }
    [HttpGet("Review")][HttpGet("Glosas")] public async Task<IActionResult> WorkQueue(Guid? documentId, bool? hasDenial, CancellationToken ct)
    {
        if (documentId is { } sourceDocumentId && sourceDocumentId != Guid.Empty)
        {
            var billingDocument = await queries.GetByDocumentIdAsync(user.TenantId, sourceDocumentId, ct);
            if (billingDocument is null)
            {
                TempData["Error"] = "O documento ainda não possui extração de faturamento hospitalar para revisão.";
            }
            else
            {
                return RedirectToAction(nameof(Details), new { id = billingDocument.Id });
            }
        }
        var denialQueue = hasDenial == true || Request.Path.Value!.EndsWith("Glosas", StringComparison.OrdinalIgnoreCase);
        ViewBag.Mode = denialQueue ? "denials" : "review";
        return await Index(null, null, denialQueue ? null : "PENDING_REVIEW", denialQueue ? true : null, null, null, null, null, null, null, null, null, ct);
    }
    [HttpGet("Extractions")]
    public Task<IActionResult> Extractions(CancellationToken ct)
    { ViewBag.Mode = "ocr"; return Index(null, null, null, null, null, null, null, null, null, null, true, null, ct); }
    [HttpGet("Export")]
    public async Task<IActionResult> Export(string? insurer, string? competence, string? status, bool? hasDenial, string? term, CancellationToken ct)
    {
        var filter = new HospitalBillingFilter(insurer, competence, status, hasDenial, term);
        var dashboard = await queries.DashboardAsync(user.TenantId, filter, ct);
        var csv = new StringBuilder("Documento;Tipo;Convenio;Competencia;Paciente protegido;Guia;Autorizacao;Lote;Procedimento;Codigo TUSS/SIGTAP;Apresentado;Aprovado;Glosado;Recuperado;Motivo da glosa;Vencimento;Confianca;Status\r\n");
        foreach (var item in dashboard.Documents)
        {
            csv.AppendJoin(';', new[]
            {
                Csv(item.Title), Csv(item.DocumentType), Csv(item.Insurer), Csv(item.Competence), Csv(item.MaskedPatient),
                Csv(item.GuideNumber), Csv(item.AuthorizationNumber), Csv(item.BatchNumber), Csv(item.ProcedureName), Csv(item.ProcedureCode),
                Number(item.PresentedAmount), Number(item.ApprovedAmount), Number(item.DeniedAmount), Number(item.RecoveredAmount),
                Csv(item.DenialReason), Csv(item.DueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                Number(item.Confidence), Csv(item.Status)
            }).Append("\r\n");
        }
        await audit.WriteAsync(user.TenantId, user.UserId, "EXPORT", "HOSPITAL_BILLING", null,
            "Exportação da fila de faturamento hospitalar", HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), new { dashboard.Documents.Count, hasFilters = filter != new HospitalBillingFilter() }, ct);
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"faturamento-hospitalar-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
    [HttpGet("Rules")]
    public IActionResult Rules() => View(new HospitalBillingRulesCatalog(
    [
        new("Guia TISS / SADT", "documents", ["guia", "TISS", "SADT", "TUSS"], ["Convênio", "Prestador", "Número da guia", "Autorização"], "Conferir beneficiário mascarado, autorização, procedimento e valores."),
        new("AIH", "report", ["AIH", "SUS", "internação"], ["Número da AIH", "CNES", "Competência", "Procedimento"], "Validar competência, caráter da internação e código SIGTAP."),
        new("APAC / BPA", "classification", ["APAC", "BPA", "SIGTAP"], ["Autorização", "CNES", "Competência", "Procedimento"], "Comparar produção apresentada com autorização e competência."),
        new("Fatura hospitalar", "report", ["fatura", "conta hospitalar", "valor apresentado"], ["Convênio", "Fatura", "Competência", "Valor apresentado"], "Reconciliar guias do lote e totais financeiros."),
        new("Demonstrativo de glosa", "warning", ["glosa", "motivo", "valor glosado"], ["Fatura", "Motivo da glosa", "Valor glosado"], "Classificar motivo, prazo e elegibilidade para recurso."),
        new("Recurso / protocolo", "protocol", ["recurso de glosa", "protocolo de envio"], ["Protocolo", "Lote", "Valor recuperado"], "Rastrear envio, prazo, resposta e recuperação financeira.")
    ],
    ["Valor aprovado + glosado não pode superar o apresentado.", "Guia, autorização e competência devem corresponder ao documento-fonte.", "Confiança inferior a 70% exige revisão humana.", "Paciente permanece mascarado em filas e listagens."]));
    [HttpGet("Details/{id:guid}")] public async Task<IActionResult> Details(Guid id, CancellationToken ct) => await queries.GetDetailsAsync(user.TenantId, id, ct) is { } item ? View(item) : NotFound();
    [HttpPost("Review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveReview(HospitalBillingReviewRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Revise os campos informados."; return RedirectToAction(nameof(Details), new { id = request.Id }); }
        if (!await queries.ReviewAsync(user.TenantId, user.UserId, request, ct))
        { TempData["Error"] = "Os valores são inconsistentes: aprovado + glosado não pode superar o apresentado, e recuperado não pode superar a glosa."; return RedirectToAction(nameof(Details), new { id = request.Id }); }
        await audit.WriteAsync(user.TenantId, user.UserId, "UPDATE", "HOSPITAL_BILLING_REVIEW", request.Id, "Revisão hospitalar registrada", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { request.Status, request.DenialStatus, request.DenialReason, request.AppealDueDate, request.ApprovedAmount, request.DeniedAmount, request.RecoveredAmount }, ct);
        TempData["Success"] = "Revisão salva e incluída no histórico.";
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }
    [HttpGet("Reports")]
    public async Task<IActionResult> Reports(CancellationToken ct) => View(await queries.ReportsAsync(user.TenantId, ct));

    [HttpGet("Reports/Export")]
    public async Task<IActionResult> ExportReports(string report = "all", CancellationToken ct = default)
    {
        var reports = await queries.ReportsAsync(user.TenantId, ct);
        var normalized = report.Trim().ToLowerInvariant();
        var sections = normalized switch
        {
            "insurer" => new[] { ("Convênio", reports.ByInsurer) },
            "competence" => new[] { ("Competência", reports.ByCompetence) },
            "provider" => new[] { ("Prestador", reports.ByProvider) },
            "review-status" => new[] { ("Status de revisão", reports.ByReviewStatus) },
            "denials" => new[] { ("Motivo da glosa", reports.Denials) },
            "all" => new[] { ("Convênio", reports.ByInsurer), ("Competência", reports.ByCompetence), ("Prestador", reports.ByProvider), ("Status de revisão", reports.ByReviewStatus), ("Motivo da glosa", reports.Denials) },
            _ => Array.Empty<(string, IReadOnlyList<HospitalBillingReportRow>)>()
        };
        if (sections.Length == 0) return BadRequest("Relatório inválido.");

        var csv = new StringBuilder("Relatorio;Grupo;Documentos;Apresentado;Aprovado;Glosado;Recuperado;Saldo pendente\r\n");
        foreach (var (label, rows) in sections)
            foreach (var row in rows)
                csv.AppendJoin(';', Csv(label), Csv(row.Label), row.Documents.ToString(CultureInfo.InvariantCulture), Number(row.Presented), Number(row.Approved), Number(row.Denied), Number(row.Recovered), Number(row.PendingRecovery)).Append("\r\n");

        await audit.WriteAsync(user.TenantId, user.UserId, "EXPORT", "HOSPITAL_BILLING_REPORT", null,
            "Exportação de relatório gerencial hospitalar", HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), new { report = normalized, sections = sections.Length, rows = sections.Sum(x => x.Item2.Count) }, ct);
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"relatorio-faturamento-hospitalar-{normalized}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
