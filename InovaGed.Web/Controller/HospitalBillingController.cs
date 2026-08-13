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
    public async Task<IActionResult> Index(string? insurer, string? competence, string? status, bool? hasDenial, string? term, CancellationToken ct)
    { var filter = new HospitalBillingFilter(insurer, competence, status, hasDenial, term); ViewBag.Filter = filter; return View(await queries.DashboardAsync(user.TenantId, filter, ct)); }
    [HttpGet("Documents")] public Task<IActionResult> Documents(string? insurer, string? competence, string? status, bool? hasDenial, string? term, CancellationToken ct) => Index(insurer, competence, status, hasDenial, term, ct);
    [HttpGet("Review")][HttpGet("Glosas")] public Task<IActionResult> WorkQueue(bool? hasDenial, CancellationToken ct)
    {
        var denialQueue = hasDenial == true || Request.Path.Value!.EndsWith("Glosas", StringComparison.OrdinalIgnoreCase);
        ViewBag.ActiveArea = denialQueue ? "glosas" : "review";
        return DashboardView(new HospitalBillingFilter(null, null, denialQueue ? null : "PENDING_REVIEW", denialQueue ? true : null), ct);
    }
    [HttpGet("Reports")]
    public async Task<IActionResult> Reports(string groupBy = "insurer", CancellationToken ct) => View(await queries.ReportAsync(user.TenantId, groupBy, ct));
    [HttpGet("Reports/Export")]
    public async Task<IActionResult> ExportReport(string groupBy = "insurer", CancellationToken ct)
    {
        var report = await queries.ReportAsync(user.TenantId, groupBy, ct);
        var csv = new StringBuilder("Agrupamento;Documentos;Com glosa;Apresentado;Aprovado;Glosado;Recuperado\r\n");
        foreach (var row in report.Rows) csv.AppendJoin(';', Csv(row.Label), row.Documents, row.WithDenial, Number(row.Presented), Number(row.Approved), Number(row.Denied), Number(row.Recovered)).Append("\r\n");
        await audit.WriteAsync(user.TenantId, user.UserId, "EXPORT", "HOSPITAL_BILLING_REPORT", null, $"Exportação de relatório por {report.GroupBy}", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { report.GroupBy, report.Rows.Count }, ct);
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"relatorio-faturamento-{report.GroupBy}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
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
    [HttpGet("Details/{id:guid}")] public async Task<IActionResult> Details(Guid id, CancellationToken ct) => await queries.GetAsync(user.TenantId, id, ct) is { } item ? View(item) : NotFound();

    private async Task<IActionResult> DashboardView(HospitalBillingFilter filter, CancellationToken ct)
    { ViewBag.Filter = filter; return View("Index", await queries.DashboardAsync(user.TenantId, filter, ct)); }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
