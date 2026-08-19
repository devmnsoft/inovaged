using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Tracking;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;
[Authorize(Policy=AppPolicies.FullAdminOnly)]
[Route("LabelTracking")]
public sealed class LabelTrackingController(IDbConnectionFactory dbFactory,ILabelTrackingService tracking,ILabelInventoryService inventory,ILabelReplacementService replacements):GedControllerBase(dbFactory)
{
 [HttpGet("")]public IActionResult Index()=>View();
 [HttpGet("Scanner")]public IActionResult Scanner()=>View();
 [HttpPost("Scan"),ValidateAntiForgeryToken]public async Task<IActionResult> Scan(string payload,string? location,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();var r=await tracking.ScanAsync(new(TenantId,uid,payload,location,"WEB",HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent),ct);if(Request.Headers.Accept.ToString().Contains("application/json",StringComparison.OrdinalIgnoreCase))return Json(r);if(r.Trace is null){TempData["Error"]=r.Message;return RedirectToAction(nameof(Scanner));}return View("Trace",r.Trace with{Status=r.Status});}
 [HttpGet("Trace")]public async Task<IActionResult> Trace(string payloadOrCode,CancellationToken ct){var x=await tracking.TraceAsync(TenantId,payloadOrCode,ct);return x is null?NotFound():View(x);}
 [HttpGet("Events")]public async Task<IActionResult> Events([FromQuery]LabelScanEventFilter f,CancellationToken ct)=>View(await tracking.ListEventsAsync(TenantId,f,ct));
 [HttpGet("Inventory")]public async Task<IActionResult> Inventory(CancellationToken ct){using var db=await OpenAsync();return View("InventoryIndex",await db.QueryAsync("select id,session_number,title,expected_location,status,started_at,(select count(*) from ged.label_inventory_item i where i.tenant_id=s.tenant_id and i.session_id=s.id and i.reg_status='A') total from ged.label_inventory_session s where tenant_id=@tid and reg_status='A' order by started_at desc",new{tid=TenantId}));}
 [HttpGet("Inventory/New")]public IActionResult InventoryNew()=>View();
 [HttpPost("Inventory/New"),ValidateAntiForgeryToken]public async Task<IActionResult> InventoryNew(string title,string? expectedLocation,string? notes,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();var id=await inventory.StartSessionAsync(new(TenantId,uid,title,expectedLocation,notes),ct);return RedirectToAction(nameof(InventoryDetails),new{id});}
 [HttpGet("Inventory/{id:guid}")]public async Task<IActionResult> InventoryDetails(Guid id,CancellationToken ct){var x=await inventory.GetSessionAsync(TenantId,id,ct);return x is null?NotFound():View(x);}
 [HttpPost("Inventory/{id:guid}/Scan"),ValidateAntiForgeryToken]public async Task<IActionResult> InventoryScan(Guid id,string payload,string? foundLocation,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();var r=await inventory.AddScanAsync(new(TenantId,id,uid,payload,foundLocation,HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent),ct);return Request.Headers.Accept.ToString().Contains("application/json",StringComparison.OrdinalIgnoreCase)?Json(r):RedirectToAction(nameof(InventoryDetails),new{id});}
 [HttpPost("Inventory/{id:guid}/Close"),ValidateAntiForgeryToken]public async Task<IActionResult> Close(Guid id,string? notes,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await inventory.CloseSessionAsync(TenantId,id,uid,notes,ct);return RedirectToAction(nameof(InventoryDetails),new{id});}
 [HttpPost("Inventory/{id:guid}/Cancel"),ValidateAntiForgeryToken]public async Task<IActionResult> Cancel(Guid id,string reason,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await inventory.CancelSessionAsync(TenantId,id,uid,reason,ct);return RedirectToAction(nameof(InventoryDetails),new{id});}
 [HttpGet("Replacements")]public async Task<IActionResult> Replacements(CancellationToken ct)=>View(await replacements.ListAsync(TenantId,ct));
 [HttpPost("Replacements/Request"),ValidateAntiForgeryToken]public async Task<IActionResult> RequestReplacement(Guid? oldPrintHistoryId,string subjectType,Guid? subjectId,string? controlNumber,string reason,string? oldTemplateCode,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await replacements.RequestReplacementAsync(new(TenantId,uid,oldPrintHistoryId,subjectType,subjectId,controlNumber,reason,oldTemplateCode),ct);return RedirectToAction(nameof(Replacements));}
 [HttpPost("Replacements/{id:guid}/Approve"),ValidateAntiForgeryToken]public async Task<IActionResult> Approve(Guid id,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await replacements.ApproveAsync(TenantId,id,uid,ct);return RedirectToAction(nameof(Replacements));}
 [HttpPost("Replacements/{id:guid}/Reject"),ValidateAntiForgeryToken]public async Task<IActionResult> Reject(Guid id,string reason,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await replacements.RejectAsync(TenantId,id,uid,reason,ct);return RedirectToAction(nameof(Replacements));}
 [HttpPost("Replacements/{id:guid}/Complete"),ValidateAntiForgeryToken]public async Task<IActionResult> Complete(Guid id,Guid newPrintHistoryId,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await replacements.CompleteAsync(TenantId,id,newPrintHistoryId,uid,ct);return RedirectToAction(nameof(Replacements));}
 [HttpGet("Events.csv")]public async Task<IActionResult> EventsCsv([FromQuery]LabelScanEventFilter f,CancellationToken ct){var rows=await tracking.ListEventsAsync(TenantId,f,ct);var csv="Data;Status;Tipo;Controle;Local\n"+string.Join('\n',rows.Select(x=>$"{x.ScannedAt:O};{x.Status};{x.SubjectType};{Csv(x.ControlNumber)};{Csv(x.LocationScanned)}"));return File(System.Text.Encoding.UTF8.GetBytes(csv),"text/csv; charset=utf-8","leituras-etiquetas.csv");} private static string Csv(string? x)=>$"\"{(x??"").Replace("\"","\"\"")}\"";
}
