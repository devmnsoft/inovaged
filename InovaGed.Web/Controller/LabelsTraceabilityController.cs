using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Route("Labels")]
public sealed class LabelsTraceabilityController(IDbConnectionFactory dbFactory, ILabelTraceabilityService traces) : GedControllerBase(dbFactory)
{
    [Authorize, HttpGet("Scanner")]
    public async Task<IActionResult> Scanner(CancellationToken ct) => View("~/Views/Labels/Scanner.cshtml", await RecentAsync(ct));

    [Authorize, ValidateAntiForgeryToken, HttpPost("Scanner/Resolve")]
    public async Task<IActionResult> Resolve(string code, string? location, CancellationToken ct)
    {
        var trace = await traces.ResolveInternalAsync(TenantId, code, ct);
        if (trace is null) return Json(new { status=LabelScanResult.Unknown, message="Etiqueta não localizada." });
        await traces.RegisterScanAsync(trace,UserId,User.Identity?.Name,"WEB",ScanResult(trace.Status),HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent,location,null,ct);
        return Json(new { status=trace.Status, traceCode=trace.TraceCode, type=trace.SubjectType, template=trace.TemplateCode, url=$"/Labels/Trace/{trace.TraceCode}" });
    }

    [Authorize, HttpGet("Trace")]
    public async Task<IActionResult> TraceIndex(CancellationToken ct) => View("~/Views/Labels/TraceIndex.cshtml",await RecentTracesAsync(ct));

    [AllowAnonymous, HttpGet("Trace/{token}")]
    public async Task<IActionResult> Trace(string token,CancellationToken ct)
    {
        LabelTracePublicInfo? trace;
        if(User.Identity?.IsAuthenticated==true) trace=await traces.ResolveInternalAsync(TenantId,token,ct);
        else trace=await traces.ResolvePublicAsync(token,ct);
        return View("~/Views/Labels/TracePublic.cshtml",trace);
    }

    [AllowAnonymous, HttpGet("/l/{token}")]
    public IActionResult Short(string token) => RedirectToAction(nameof(Trace),new{token});

    [Authorize, HttpGet("Trace/{token}/History")]
    public async Task<IActionResult> History(string token,CancellationToken ct) => await Trace(token,ct);

    [Authorize, ValidateAntiForgeryToken, HttpPost("Trace/{token}/RegisterScan")]
    public async Task<IActionResult> RegisterScan(string token,string? location,string? notes,CancellationToken ct)
    { var trace=await traces.ResolveInternalAsync(TenantId,token,ct);if(trace is null)return NotFound();await traces.RegisterScanAsync(trace,UserId,User.Identity?.Name,"MANUAL",ScanResult(trace.Status),HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent,location,notes,ct);return RedirectToAction(nameof(Trace),new{token=trace.TraceCode}); }

    [Authorize, HttpGet("Replacements")]
    public async Task<IActionResult> Replacements(CancellationToken ct){using var db=await OpenAsync();var rows=await db.QueryAsync(new CommandDefinition("select r.id,o.trace_code old_code,n.trace_code new_code,r.reason,r.status,r.requested_at from ged.label_replacement_event r join ged.label_trace_identity o on o.id=r.old_trace_id left join ged.label_trace_identity n on n.id=r.new_trace_id where r.tenant_id=@tid and r.reg_status='A' order by r.requested_at desc limit 200",new{tid=TenantId},cancellationToken:ct));return View("~/Views/Labels/Replacements.cshtml",rows);}
    [Authorize, HttpGet("Replacements/Create")] public IActionResult ReplacementCreate()=>View("~/Views/Labels/ReplacementCreate.cshtml");
    [Authorize, ValidateAntiForgeryToken, HttpPost("Replacements/Create")]
    public async Task<IActionResult> ReplacementCreate(string labelCode,string reason,string newTemplateCode,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await traces.ReplaceAsync(TenantId,labelCode,reason,newTemplateCode,uid,User.Identity?.Name,ct);return RedirectToAction(nameof(Replacements));}
    [Authorize, HttpGet("Replacements/{id:guid}")] public async Task<IActionResult> Replacement(Guid id,CancellationToken ct){using var db=await OpenAsync();var row=await db.QuerySingleOrDefaultAsync(new CommandDefinition("select * from ged.label_replacement_event where tenant_id=@tid and id=@id and reg_status='A'",new{tid=TenantId,id},cancellationToken:ct));return row is null?NotFound():View("~/Views/Labels/ReplacementDetails.cshtml",row);}

    [Authorize, HttpGet("Quality/QrCode")] public IActionResult QrQuality()=>View("~/Views/Labels/QrQuality.cshtml");
    [Authorize, ValidateAntiForgeryToken, HttpPost("Quality/QrCode/Validate")]
    public async Task<IActionResult> QrQualityValidate(string templateCode,bool hasQr,decimal sizeMm,bool hasQuietZone,string sampleUrl,CancellationToken ct)
    {var issues=new List<(string,string,string)>();if(!hasQr)issues.Add(("QR_MISSING","HIGH","QR Code ausente"));if(sizeMm<20)issues.Add(("QR_TOO_SMALL","HIGH","QR Code menor que 20 mm"));if(!hasQuietZone)issues.Add(("QUIET_ZONE","MEDIUM","Quiet zone insuficiente"));if(!sampleUrl.StartsWith("/l/",StringComparison.OrdinalIgnoreCase)||sampleUrl.Contains("tenant_id",StringComparison.OrdinalIgnoreCase)||sampleUrl.Contains("document_id",StringComparison.OrdinalIgnoreCase)||sampleUrl.Contains("box_id",StringComparison.OrdinalIgnoreCase))issues.Add(("UNSAFE_PAYLOAD","CRITICAL","Payload não usa somente a rota curta segura"));using var db=await OpenAsync();foreach(var i in issues)await db.ExecuteAsync(new CommandDefinition("insert into ged.label_qr_quality_issue(tenant_id,template_code,issue_type,severity,title,recommended_action) values(@tid,@template,@type,@severity,@title,'Ajuste o modelo antes de imprimir.')",new{tid=TenantId,template=templateCode,type=i.Item1,severity=i.Item2,title=i.Item3},cancellationToken:ct));TempData[issues.Count==0?"Success":"Error"]=issues.Count==0?"QR Code aprovado.":$"{issues.Count} problema(s) registrado(s).";return RedirectToAction(nameof(QrQuality));}

    private async Task<IEnumerable<dynamic>> RecentAsync(CancellationToken ct){using var db=await OpenAsync();return await db.QueryAsync(new CommandDefinition("select i.trace_code,e.scan_result,e.scanned_at from ged.label_scan_event e join ged.label_trace_identity i on i.id=e.trace_id where e.tenant_id=@tid order by e.scanned_at desc limit 10",new{tid=TenantId},cancellationToken:ct));}
    private async Task<IEnumerable<dynamic>> RecentTracesAsync(CancellationToken ct){using var db=await OpenAsync();return await db.QueryAsync(new CommandDefinition("select trace_code,subject_type,template_code,status,issued_at from ged.label_trace_identity where tenant_id=@tid and reg_status='A' order by issued_at desc limit 200",new{tid=TenantId},cancellationToken:ct));}
    private static string ScanResult(string status)=>status switch{LabelTraceStatus.Replaced=>LabelScanResult.Replaced,LabelTraceStatus.Revoked=>LabelScanResult.Revoked,_=>LabelScanResult.Valid};
}
