using System.Diagnostics;
using System.Text;
using InovaGed.Application.Audit;
using InovaGed.Application.HospitalIntelligence;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.HospitalIntelligence;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
[Route("HospitalIntelligence")]
public sealed class HospitalIntelligenceController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IHospitalIntelligenceService _service;
    private readonly IAuditWriter _audit;
    private readonly ILogger<HospitalIntelligenceController> _logger;

    public HospitalIntelligenceController(ICurrentUser currentUser, IHospitalIntelligenceService service, IAuditWriter audit, ILogger<HospitalIntelligenceController> logger)
    {
        _currentUser = currentUser;
        _service = service;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(HospitalIntelligenceFilterVM filter, CancellationToken ct)
    {
        var correlationId = CorrelationId;
        try
        {
            var dashboard = await _service.GetDashboardAsync(Map(filter), ct);
            await WriteAuditAsync("VIEW", "Acesso à Inteligência Hospitalar por OCR", new { filter.From, filter.To, filter.FolderId, filter.Sector, filter.DocumentType, dashboard.TotalDocuments, correlationId }, ct);
            ViewBag.Filter = filter;
            return View(dashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao abrir HospitalIntelligence. Tenant={TenantId} User={UserId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, correlationId);
            await WriteErrorAuditAsync("Erro ao abrir Inteligência Hospitalar por OCR", ex, ct);
            TempData["Error"] = "Não foi possível carregar a Inteligência Hospitalar por OCR. Informe o código de rastreio ao suporte.";
            ViewBag.Filter = filter;
            return View(new HospitalIntelligenceDashboardDto { Warnings = [$"Indicadores indisponíveis no momento. Código de rastreio: {correlationId}"] });
        }
    }

    [HttpGet("Data")]
    public async Task<IActionResult> Data(HospitalIntelligenceFilterVM filter, CancellationToken ct)
    {
        var correlationId = CorrelationId;
        try
        {
            var data = await _service.GetDashboardAsync(Map(filter), ct);
            return Json(new { success = true, data, correlationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro JSON HospitalIntelligence. Tenant={TenantId} CorrelationId={CorrelationId}", _currentUser.TenantId, correlationId);
            await WriteErrorAuditAsync("Erro ao consultar dados da Inteligência Hospitalar por OCR", ex, ct);
            return Json(new { success = false, message = "Não foi possível carregar os indicadores.", correlationId });
        }
    }

    [HttpGet("ExportCsv")]
    public async Task<IActionResult> ExportCsv(HospitalIntelligenceFilterVM filter, CancellationToken ct)
    {
        var correlationId = CorrelationId;
        try
        {
            var dashboard = await _service.GetDashboardAsync(Map(filter), ct);
            var csv = BuildCsv(dashboard);
            await WriteAuditAsync("REPORT_PRINT", "Exportação CSV da Inteligência Hospitalar por OCR", new { filter.From, filter.To, dashboard.TotalDocuments, correlationId }, ct);
            return File(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv), "text/csv; charset=utf-8", $"inteligencia-hospitalar-ocr-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao exportar HospitalIntelligence. Tenant={TenantId} CorrelationId={CorrelationId}", _currentUser.TenantId, correlationId);
            await WriteErrorAuditAsync("Erro ao exportar CSV da Inteligência Hospitalar por OCR", ex, ct);
            TempData["Error"] = "Não foi possível exportar o relatório CSV.";
            return RedirectToAction(nameof(Index));
        }
    }

    private HospitalIntelligenceFilter Map(HospitalIntelligenceFilterVM vm) => new()
    {
        TenantId = _currentUser.TenantId,
        From = vm.From,
        To = vm.To,
        FolderId = vm.FolderId,
        Sector = vm.Sector,
        DocumentType = vm.DocumentType,
        Search = vm.Search,
        Top = vm.Top,
        RefreshCache = vm.RefreshCache
    };

    private async Task WriteAuditAsync(string action, string summary, object data, CancellationToken ct)
        => await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, action, "HOSPITAL_INTELLIGENCE", null, summary, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), data, ct);

    private async Task WriteErrorAuditAsync(string summary, Exception ex, CancellationToken ct)
    {
        try
        {
            await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPDATE", "HOSPITAL_INTELLIGENCE", null, summary, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { EventType = "ERROR", Error = ex.GetType().Name, CorrelationId }, ct);
        }
        catch (Exception auditEx)
        {
            _logger.LogWarning(auditEx, "Falha ao auditar erro de HospitalIntelligence. CorrelationId={CorrelationId}", CorrelationId);
        }
    }

    private static string BuildCsv(HospitalIntelligenceDashboardDto d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Seção;Indicador;Valor;Descrição");
        Add(sb, "KPI geral", "Total de documentos", d.TotalDocuments.ToString(), "Documentos reais no recorte");
        Add(sb, "KPI geral", "Documentos com OCR", d.DocumentsWithOcr.ToString(), $"{d.OcrCoveragePercent:N2}% de cobertura");
        Add(sb, "KPI geral", "Documentos sem OCR", d.DocumentsWithoutOcr.ToString(), "Pendências de OCR");
        Add(sb, "KPI geral", "Documentos classificados", d.ClassifiedDocuments.ToString(), $"{d.ClassificationCoveragePercent:N2}% de cobertura");
        Add(sb, "KPI geral", "Qualidade da base", $"{d.DataQualityScore:N2}%", "Score ponderado");
        foreach (var k in d.OcrKpis) Add(sb, "Status OCR", k.Status, k.Count.ToString(), k.Description);
        foreach (var t in d.ClinicalTerms.Take(30)) Add(sb, "Termos clínicos documentais", t.Term, t.DocumentCount.ToString(), $"{t.Category}; ocorrências: {t.Occurrences}; risco: {t.RiskLevel}");
        foreach (var f in d.FinancialKpis.Take(30)) Add(sb, "Sinais financeiros documentais", f.Indicator, f.DocumentCount.ToString(), $"{f.Description}; valor estimado: {f.EstimatedValue?.ToString("N2") ?? "não calculado"}; risco: {f.RiskLevel}");
        foreach (var o in d.OperationalKpis) Add(sb, "Gargalos operacionais", o.Indicator, o.Value, $"{o.Description} {o.Recommendation}");
        foreach (var a in d.Alerts) Add(sb, "Alertas executivos", a.Title, a.Severity, $"{a.Description} {a.Recommendation}");
        foreach (var w in d.Warnings) Add(sb, "Avisos", "Aviso", "", w);
        return sb.ToString();
    }

    private static void Add(StringBuilder sb, string section, string indicator, string value, string description)
        => sb.AppendLine($"{Csv(section)};{Csv(indicator)};{Csv(value)};{Csv(description)}");

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"").ReplaceLineEndings(" ")}\"";
    private string CorrelationId => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
