using System.Text;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Intelligence;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy=AppPolicies.FullAdminOnly)]
[Route("LabelIntelligence")]
public sealed class LabelIntelligenceController(IDbConnectionFactory db,ILabelIntelligenceService intelligence,ILabelAlertService alerts,ILabelCustodyService custody):GedControllerBase(db)
{
 [HttpGet("")] public IActionResult Index()=>RedirectToAction(nameof(Dashboard));
 [HttpGet("Dashboard")] public async Task<IActionResult> Dashboard([FromQuery]LabelIntelligenceFilter filter,CancellationToken ct){ViewBag.Filter=filter;return View(await intelligence.GetDashboardAsync(TenantId,filter,ct));}
 [HttpGet("Divergences")] public async Task<IActionResult> Divergences([FromQuery]LabelIntelligenceFilter filter,CancellationToken ct)=>View(await intelligence.ListDivergencesAsync(TenantId,filter,ct));
 [HttpGet("NeverScanned")] public async Task<IActionResult> NeverScanned([FromQuery]LabelIntelligenceFilter filter,CancellationToken ct)=>View(await intelligence.ListNeverScannedAsync(TenantId,filter,ct));
 [HttpGet("WithoutLabel")] public async Task<IActionResult> WithoutLabel(CancellationToken ct,string subjectType="BOX"){ViewBag.SubjectType=subjectType;return View(await intelligence.ListObjectsWithoutLabelAsync(TenantId,subjectType,ct));}
 [HttpGet("Alerts")] public async Task<IActionResult> Alerts([FromQuery]LabelAlertFilter filter,CancellationToken ct)=>View(await alerts.ListAsync(TenantId,filter,ct));
 [HttpPost("Alerts/Detect"),ValidateAntiForgeryToken] public async Task<IActionResult> Detect(CancellationToken ct){var count=await alerts.DetectAlertsAsync(TenantId,ct);TempData["Success"]=$"Detecção concluída: {count} alerta(s) criado(s).";return RedirectToAction(nameof(Alerts));}
 [HttpPost("Alerts/{id:guid}/Resolve"),ValidateAntiForgeryToken] public async Task<IActionResult> Resolve(Guid id,string notes,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await alerts.ResolveAsync(TenantId,id,uid,notes,ct);return RedirectToAction(nameof(Alerts));}
 [HttpPost("Alerts/{id:guid}/Ignore"),ValidateAntiForgeryToken] public async Task<IActionResult> Ignore(Guid id,string notes,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await alerts.IgnoreAsync(TenantId,id,uid,notes,ct);return RedirectToAction(nameof(Alerts));}
 [HttpGet("Custody")] public IActionResult Custody()=>View(new LabelCustodyTimeline("",null,null,[]));
 [HttpGet("CustodyByControl")] public async Task<IActionResult> CustodyByControl(string controlNumber,CancellationToken ct){if(string.IsNullOrWhiteSpace(controlNumber))return RedirectToAction(nameof(Custody));return View("Custody",await custody.GetTimelineByControlAsync(TenantId,controlNumber.Trim(),ct));}
 [HttpGet("Custody/{subjectType}/{subjectId:guid}")] public async Task<IActionResult> Custody(string subjectType,Guid subjectId,CancellationToken ct)=>View(await custody.GetTimelineAsync(TenantId,subjectType,subjectId,ct));
 [HttpGet("Export")] public async Task<IActionResult> Export(CancellationToken ct,string report="never-scanned",string subjectType="BOX"){IEnumerable<string> lines;switch(report){case "divergences":var d=await intelligence.ListDivergencesAsync(TenantId,new(),ct);lines=new[]{"Data;Tipo;Controle;Esperada;Encontrada"}.Concat(d.Select(x=>$"{x.DetectedAt:O};{Csv(x.SubjectType)};{Csv(x.ControlNumber)};{Csv(x.ExpectedLocation)};{Csv(x.FoundLocation)}"));break;case "without-label":var w=await intelligence.ListObjectsWithoutLabelAsync(TenantId,subjectType,ct);lines=new[]{"Tipo;Controle;Localização;Criação"}.Concat(w.Select(x=>$"{Csv(x.SubjectType)};{Csv(x.ControlNumber)};{Csv(x.Location)};{x.CreatedAt:O}"));break;case "alerts":var a=await alerts.ListAsync(TenantId,new(LabelAlertStatus.Open),ct);lines=new[]{"Data;Severidade;Tipo;Controle;Título;Mensagem"}.Concat(a.Select(x=>$"{x.DetectedAt:O};{x.Severity};{x.AlertType};{Csv(x.ControlNumber)};{Csv(x.Title)};{Csv(x.Message)}"));break;default:var n=await intelligence.ListNeverScannedAsync(TenantId,new(),ct);lines=new[]{"Impressão;Tipo;Controle;Modelo;Localização;Usuário"}.Concat(n.Select(x=>$"{x.PrintedAt:O};{x.SubjectType};{Csv(x.ControlNumber)};{Csv(x.TemplateCode)};{Csv(x.Location)};{x.PrintedBy}"));break;}return File(Encoding.UTF8.GetBytes('\uFEFF'+string.Join('\n',lines)),"text/csv; charset=utf-8",$"inteligencia-etiquetas-{report}.csv");}
 private static string Csv(string? value)=>$"\"{(value??"").Replace("\"","\"\"")}\"";
}
