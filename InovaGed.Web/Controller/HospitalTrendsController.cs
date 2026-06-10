using System.Text;
using InovaGed.Application.Audit;
using InovaGed.Application.HospitalTrends;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.HospitalTrends;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
[Route("HospitalTrends")]
public sealed class HospitalTrendsController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IHospitalTrendsService _service;
    private readonly IAuditWriter _audit;
    private readonly ILogger<HospitalTrendsController> _logger;

    public HospitalTrendsController(ICurrentUser currentUser, IHospitalTrendsService service, IAuditWriter audit, ILogger<HospitalTrendsController> logger)
    {
        _currentUser = currentUser;
        _service = service;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(HospitalTrendsFilterVM filter, CancellationToken ct)
    {
        var correlationId = CorrelationId;
        try
        {
            var dashboard = await _service.GetDashboardAsync(Map(filter), ct);
            await WriteAuditAsync("VIEW", "Acesso à Central de Alertas e Tendências Hospitalares", new { EventType = "INFO", filter.From, filter.To, filter.CompareFrom, filter.CompareTo, filter.FolderId, filter.Sector, filter.DocumentType, filter.Category, dashboard.TotalDocumentsCurrent, correlationId }, ct);
            ViewBag.Filter = filter;
            return View(dashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao abrir HospitalTrends. Tenant={TenantId} User={UserId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, correlationId);
            await WriteErrorAuditAsync("Erro ao abrir Central de Alertas e Tendências Hospitalares", ex, ct);
            TempData["Error"] = "Não foi possível carregar Alertas e Tendências Hospitalares.";
            ViewBag.Filter = filter;
            return View(new HospitalTrendsDashboardDto { Warnings = [$"Indicadores indisponíveis no momento. Código de rastreio: {correlationId}"] });
        }
    }

    [HttpGet("Data")]
    public async Task<IActionResult> Data(HospitalTrendsFilterVM filter, CancellationToken ct)
    {
        var correlationId = CorrelationId;
        try
        {
            var dashboard = await _service.GetDashboardAsync(Map(filter), ct);
            return Json(new { success = true, data = dashboard, correlationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro JSON HospitalTrends. Tenant={TenantId} CorrelationId={CorrelationId}", _currentUser.TenantId, correlationId);
            await WriteErrorAuditAsync("Erro ao consultar dados da Central de Alertas e Tendências Hospitalares", ex, ct);
            return Json(new { success = false, message = "Não foi possível carregar os indicadores.", correlationId });
        }
    }

    [HttpGet("ExportCsv")]
    public async Task<IActionResult> ExportCsv(HospitalTrendsFilterVM filter, CancellationToken ct)
    {
        var correlationId = CorrelationId;
        try
        {
            var dashboard = await _service.GetDashboardAsync(Map(filter), ct);
            var csv = BuildCsv(dashboard);
            await WriteAuditAsync("REPORT_PRINT", "Exportação CSV da Central de Alertas e Tendências Hospitalares", new { EventType = "INFO", filter.From, filter.To, filter.CompareFrom, filter.CompareTo, dashboard.TotalDocumentsCurrent, dashboard.TotalAlerts, correlationId }, ct);
            return File(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv), "text/csv; charset=utf-8", $"alertas-tendencias-hospitalares-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao exportar HospitalTrends. Tenant={TenantId} CorrelationId={CorrelationId}", _currentUser.TenantId, correlationId);
            await WriteErrorAuditAsync("Erro ao exportar CSV da Central de Alertas e Tendências Hospitalares", ex, ct);
            return Json(new { success = false, message = "Não foi possível exportar o CSV.", correlationId });
        }
    }

    [HttpGet("AlertDetails/{alertId:guid}")]
    public async Task<IActionResult> AlertDetails(Guid alertId, CancellationToken ct)
    {
        var correlationId = CorrelationId;
        try
        {
            var alerts = await _service.GetAlertsAsync(Map(new HospitalTrendsFilterVM()), ct);
            var alert = alerts.FirstOrDefault(a => a.Id == alertId);
            if (alert is null)
                return Json(new { success = false, message = "Alerta não localizado no recorte padrão.", correlationId });

            return Json(new { success = true, data = alert, correlationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro AlertDetails HospitalTrends. AlertId={AlertId} Tenant={TenantId} CorrelationId={CorrelationId}", alertId, _currentUser.TenantId, correlationId);
            await WriteErrorAuditAsync("Erro ao consultar detalhe de alerta hospitalar", ex, ct);
            return Json(new { success = false, message = "Não foi possível consultar o alerta.", correlationId });
        }
    }

    private HospitalTrendsFilter Map(HospitalTrendsFilterVM vm) => new()
    {
        TenantId = _currentUser.TenantId,
        From = vm.From,
        To = vm.To,
        CompareFrom = vm.CompareFrom,
        CompareTo = vm.CompareTo,
        FolderId = vm.FolderId,
        Sector = vm.Sector,
        DocumentType = vm.DocumentType,
        Category = vm.Category,
        Top = vm.Top,
        RefreshCache = vm.RefreshCache
    };

    private async Task WriteAuditAsync(string action, string summary, object data, CancellationToken ct)
        => await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, action, "HOSPITAL_TRENDS", null, summary, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), data, ct);

    private async Task WriteErrorAuditAsync(string summary, Exception ex, CancellationToken ct)
    {
        try
        {
            await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPDATE", "HOSPITAL_TRENDS", null, summary, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { EventType = "ERROR", Error = ex.GetType().Name, CorrelationId }, ct);
        }
        catch (Exception auditEx)
        {
            _logger.LogWarning(auditEx, "Falha ao auditar erro de HospitalTrends. CorrelationId={CorrelationId}", CorrelationId);
        }
    }

    private static string BuildCsv(HospitalTrendsDashboardDto d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Seção;Indicador;Atual;Anterior;Variação;Severidade/Status;Descrição/Recomendação");
        Add(sb, "Resumo", "Documentos", d.TotalDocumentsCurrent.ToString(), d.TotalDocumentsPrevious.ToString(), d.VariationPercent, string.Empty, $"Período {d.PeriodLabel} comparado a {d.ComparePeriodLabel}");
        foreach (var a in d.Alerts)
            Add(sb, "Alertas", a.Title, a.RelatedDocumentCount.ToString(), string.Empty, null, a.Severity, $"{a.Description} {a.Recommendation}");
        foreach (var t in d.TermTrends)
            Add(sb, "Tendências de termos", $"{t.Term} ({t.Category})", t.CurrentCount.ToString(), t.PreviousCount.ToString(), t.VariationPercent, t.RiskLevel, t.Interpretation);
        foreach (var s in d.SectorTrends)
            Add(sb, "Tendências por setor", s.Sector, s.CurrentDocuments.ToString(), s.PreviousDocuments.ToString(), s.VariationPercent, $"OCR pendente: {s.PendingOcr}; Sem classificação: {s.Unclassified}", s.Interpretation);
        foreach (var o in d.OperationalTrends)
            Add(sb, "Gargalos operacionais", o.Indicator, o.CurrentValue.ToString(), o.PreviousValue.ToString(), o.VariationPercent, o.Status, o.Recommendation);
        return sb.ToString();
    }

    private static void Add(StringBuilder sb, string section, string indicator, string current, string previous, decimal? variation, string status, string description)
        => sb.AppendLine(string.Join(';', Csv(section), Csv(indicator), Csv(current), Csv(previous), variation.HasValue ? variation.Value.ToString("N2") + "%" : string.Empty, Csv(status), Csv(description)));

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}" + "\"";
    private string CorrelationId => HttpContext.TraceIdentifier;
}
