using InovaGed.Application.Audit;
using InovaGed.Application.Governance;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Governance")]
public sealed class GovernanceController(IGovernanceDashboardService dashboard, IGovernanceAuditService auditService, IGovernanceAlertService alerts, IGovernanceEvidenceService evidence, IGovernanceReportService reports, ICurrentUser currentUser, IAuditWriter audit, ILogger<GovernanceController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => await Safe(async () => { await WriteAudit("GOVERNANCE_DASHBOARD_VIEWED", null, ct); return View(await dashboard.GetDashboardAsync(currentUser.TenantId,currentUser.UserId,ct)); }, "GOVERNANCE_SCHEMA_INCOMPLETE", ct);
    [HttpGet("Audit")]
    public async Task<IActionResult> Audit([FromQuery] GovernanceAuditFilterInput input,CancellationToken ct)=>await Safe(async()=>{await WriteAudit("GOVERNANCE_AUDIT_VIEWED",null,ct);return View(await auditService.ListAsync(new(currentUser.TenantId,input.From,input.To,input.User,input.EventType,input.Module,input.DocumentId,input.BoxId,input.Ip,input.CorrelationId,input.Search),ct));},"GOVERNANCE_AUDIT_LOAD_FAILED",ct);
    [HttpGet("Lgpd")]
    public async Task<IActionResult> Lgpd(CancellationToken ct)=>await Safe(async()=>{await WriteAudit("GOVERNANCE_LGPD_VIEWED",null,ct);var model=await alerts.ListAsync(new(currentUser.TenantId,Type:GovernanceAlertType.SensitiveDataDetected),ct);return View(model);},"GOVERNANCE_SCHEMA_INCOMPLETE",ct);
    [HttpGet("Risks")]
    public async Task<IActionResult> Risks(CancellationToken ct)=>await Safe(async()=>View(await dashboard.GetDashboardAsync(currentUser.TenantId,currentUser.UserId,ct)),"GOVERNANCE_SCHEMA_INCOMPLETE",ct);
    [HttpGet("Alerts")]
    public async Task<IActionResult> Alerts([FromQuery]string? status,[FromQuery]string? severity,[FromQuery]string? type,[FromQuery]string? sourceType,CancellationToken ct)=>await Safe(async()=>View(await alerts.ListAsync(new(currentUser.TenantId,status,severity,type,sourceType),ct)),"GOVERNANCE_SCHEMA_INCOMPLETE",ct);
    [HttpPost("Alerts/{id:guid}/Resolve")][ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(Guid id,[FromForm]string notes,CancellationToken ct){if(string.IsNullOrWhiteSpace(notes)){TempData["Warning"]="Informe a observação obrigatória.";return RedirectToAction(nameof(Alerts));}await alerts.ResolveAsync(currentUser.TenantId,id,currentUser.UserId,notes,ct);await WriteAudit("GOVERNANCE_ALERT_RESOLVED",id,ct);TempData["Success"]="Alerta resolvido com rastreabilidade.";return RedirectToAction(nameof(Alerts));}
    [HttpGet("Evidence")]
    public async Task<IActionResult> Evidence([FromQuery]string? sourceType,[FromQuery]Guid? sourceId,CancellationToken ct)=>await Safe(async()=>View(await evidence.ListBySourceAsync(currentUser.TenantId,sourceType,sourceId,ct)),"GOVERNANCE_SCHEMA_INCOMPLETE",ct);
    [HttpPost("Evidence/Register")][ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterEvidence([FromForm]string sourceType,[FromForm]Guid? sourceId,[FromForm]string title,[FromForm]string? description,CancellationToken ct){if(string.IsNullOrWhiteSpace(sourceType)||string.IsNullOrWhiteSpace(title)){TempData["Warning"]="Fonte e título são obrigatórios.";return RedirectToAction(nameof(Evidence));}var id=await evidence.RegisterAsync(new(currentUser.TenantId,sourceType,sourceId,title,description,null,currentUser.UserId,currentUser.Email),ct);await WriteAudit("GOVERNANCE_EVIDENCE_REGISTERED",id,ct);TempData["Success"]="Evidência registrada e protegida por hash.";return RedirectToAction(nameof(Evidence));}
    [HttpGet("Timeline")]
    public async Task<IActionResult> Timeline([FromQuery]DateTimeOffset? from,[FromQuery]DateTimeOffset? to,[FromQuery]string? module,CancellationToken ct)=>await Safe(async()=>{await WriteAudit("GOVERNANCE_TIMELINE_VIEWED",null,ct);return View(await auditService.ListAsync(new(currentUser.TenantId,from,to,Module:module),ct));},"GOVERNANCE_AUDIT_LOAD_FAILED",ct);
    [HttpGet("Reports")]
    public async Task<IActionResult> Reports([FromQuery]string reportType="documents-without-ocr",CancellationToken ct)=>await Safe(async()=>View(await reports.GenerateAsync(new(currentUser.TenantId,reportType),ct)),"GOVERNANCE_REPORT_FAILED",ct);
    [HttpGet("Export")]
    public async Task<IActionResult> Export([FromQuery]string reportType,CancellationToken ct){try{var bytes=await reports.ExportCsvAsync(new(currentUser.TenantId,reportType,UserId:currentUser.UserId,UserName:currentUser.Email),ct);await WriteAudit("GOVERNANCE_REPORT_EXPORTED",null,ct);return File(bytes,"text/csv; charset=utf-8",$"governance-{reportType}-{DateTime.UtcNow:yyyyMMdd}.csv");}catch(ArgumentException){return BadRequest("Tipo de relatório inválido.");}catch(Exception ex){logger.LogError(ex,"GOVERNANCE_EXPORT_FAILED");TempData["Warning"]="Não foi possível exportar. Verifique a prontidão do banco.";return RedirectToAction(nameof(Reports));}}
    private async Task<IActionResult> Safe(Func<Task<IActionResult>> action,string incident,CancellationToken ct){try{return await action();}catch(Exception ex){logger.LogError(ex,"{Incident}",incident);ViewData["GovernanceError"]="A estrutura desta área ainda não está disponível. Valide a prontidão do banco.";return View();}}
    private Task WriteAudit(string action,Guid? id,CancellationToken ct)=>audit.WriteAsync(currentUser.TenantId,currentUser.UserId,action,"GOVERNANCE",id,"Ação de governança documental",HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),null,ct);
}
public sealed class GovernanceAuditFilterInput{public DateTimeOffset? From{get;set;}public DateTimeOffset? To{get;set;}public string? User{get;set;}public string? EventType{get;set;}public string? Module{get;set;}public Guid? DocumentId{get;set;}public Guid? BoxId{get;set;}public string? Ip{get;set;}public string? CorrelationId{get;set;}public string? Search{get;set;}}
