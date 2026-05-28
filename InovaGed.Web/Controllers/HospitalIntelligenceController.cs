using System.Diagnostics;
using System.Text;
using InovaGed.Application.Audit;
using InovaGed.Application.HospitalIntelligence;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.HospitalIntelligence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Authorize(Roles = "ADMIN,ADMINISTRATOR")]
[Route("HospitalIntelligence")]
public sealed class HospitalIntelligenceController : Controller
{
    private readonly ICurrentUser _currentUser; private readonly IHospitalIntelligenceService _service; private readonly IAuditWriter _audit; private readonly ILogger<HospitalIntelligenceController> _logger;
    public HospitalIntelligenceController(ICurrentUser currentUser, IHospitalIntelligenceService service, IAuditWriter audit, ILogger<HospitalIntelligenceController> logger){_currentUser=currentUser;_service=service;_audit=audit;_logger=logger;}
    [HttpGet("")] public async Task<IActionResult> Index(HospitalIntelligenceFilterVM filter, CancellationToken ct){try{var f=Map(filter); var dashboard=await _service.GetDashboardAsync(f,ct); await _audit.WriteAsync(_currentUser.TenantId,_currentUser.UserId,"VIEW","HOSPITAL_INTELLIGENCE",null,"Acesso ao painel",HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),new{filter.From,filter.To,dashboard.TotalDocuments},ct); ViewBag.Filter=filter; return View(dashboard);}catch(Exception ex){_logger.LogError(ex,"Erro ao abrir HospitalIntelligence."); TempData["Error"]="Não foi possível carregar a Inteligência Hospitalar."; ViewBag.Filter=filter; return View(new HospitalIntelligenceDashboardDto{Warning="Indicadores indisponíveis."});}}
    [HttpGet("Data")] public async Task<IActionResult> Data(HospitalIntelligenceFilterVM filter, CancellationToken ct)=> await Wrap(async ()=> await _service.GetDashboardAsync(Map(filter),ct));
    [HttpGet("Terms")] public async Task<IActionResult> Terms(HospitalIntelligenceFilterVM filter, CancellationToken ct)=> await Wrap(async ()=> await _service.GetClinicalTermsAsync(Map(filter),ct));
    [HttpGet("Financial")] public async Task<IActionResult> Financial(HospitalIntelligenceFilterVM filter, CancellationToken ct)=> await Wrap(async ()=> await _service.GetFinancialSignalsAsync(Map(filter),ct));
    [HttpGet("Operational")] public async Task<IActionResult> Operational(HospitalIntelligenceFilterVM filter, CancellationToken ct)=> await Wrap(async ()=> await _service.GetOperationalSignalsAsync(Map(filter),ct));
    [HttpGet("ExportCsv")] public async Task<IActionResult> ExportCsv(HospitalIntelligenceFilterVM filter, CancellationToken ct){var f=Map(filter);var d=await _service.GetDashboardAsync(f,ct);var sb=new StringBuilder();sb.AppendLine("Indicador,Valor");foreach(var c in d.Cards) sb.AppendLine($"\"{c.Title}\",\"{c.Value}\"");sb.AppendLine("Termo clínico,Contagem");foreach(var t in d.ClinicalTerms.Take(10)) sb.AppendLine($"\"{t.Term}\",{t.Count}");sb.AppendLine("Sinal financeiro,Contagem");foreach(var t in d.FinancialSignals.Take(10)) sb.AppendLine($"\"{t.Indicator}\",{t.Count}");sb.AppendLine("Alerta,Tipo,Risco");foreach(var a in d.Alerts.Take(20)) sb.AppendLine($"\"{a.Title}\",\"{a.AlertType}\",\"{a.RiskLevel}\""); await _audit.WriteAsync(_currentUser.TenantId,_currentUser.UserId,"REPORT_PRINT","HOSPITAL_INTELLIGENCE",null,"Exportação CSV",HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),new{filter.From,filter.To,d.TotalDocuments},ct); return File(Encoding.UTF8.GetBytes(sb.ToString()),"text/csv","hospital-intelligence.csv");}
    private HospitalIntelligenceFilter Map(HospitalIntelligenceFilterVM vm)=>new(){TenantId=_currentUser.TenantId,From=vm.From,To=vm.To,FolderId=vm.FolderId,Sector=vm.Sector,DocumentType=vm.DocumentType,Search=vm.Search,Top=vm.Top,BypassCache=vm.Refresh};
    private async Task<IActionResult> Wrap(Func<Task<object>> fn){var cid=Activity.Current?.Id ?? HttpContext.TraceIdentifier; try{return Json(new{success=true,data=await fn(),correlationId=cid});}catch(Exception ex){return Json(new{success=false,message="Não foi possível carregar os indicadores.",errorStep="Consulta",errorLog=ex.Message,correlationId=cid});}}
}
