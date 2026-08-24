using System.Security.Claims;
using System.Text;
using InovaGed.Application.SystemHealth.Incidents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("SystemIncidents")]
public sealed class SystemIncidentsController(ISystemIncidentService incidents,IRouteHealthRecorder routes) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery]SystemIncidentFilter filter,CancellationToken ct)=>View(await incidents.ListAsync(filter with{TenantId=Tenant()},ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id,CancellationToken ct){var item=await incidents.GetAsync(id,ct);return item is null?NotFound():View(item);}
    [HttpPost("{id:guid}/Resolve"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(Guid id,string notes,CancellationToken ct){await incidents.ResolveAsync(id,UserId(),notes,ct);return RedirectToAction(nameof(Details),new{id});}
    [HttpPost("{id:guid}/Ignore"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Ignore(Guid id,string reason,CancellationToken ct){await incidents.IgnoreAsync(id,UserId(),reason,ct);return RedirectToAction(nameof(Details),new{id});}
    [HttpGet("Export")]
    public async Task<IActionResult> Export(CancellationToken ct){var rows=await incidents.ListAsync(new(TenantId:Tenant(),Limit:5000),ct);var csv=new StringBuilder("Severity,Type,Status,Title,Route,Occurrences,CorrelationId,LastSeenAt\n");foreach(var x in rows)csv.AppendLine(string.Join(',',new[]{x.Severity,x.IncidentType,x.Status,x.Title,x.Path,x.OccurrenceCount.ToString(),x.CorrelationId,x.LastSeenAt.ToString("O")}.Select(Escape)));return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),"text/csv","system-incidents.csv");}
    [HttpGet("RouteHealth")]
    public async Task<IActionResult> RouteHealth(CancellationToken ct)=>View(await routes.ListAsync(ct));
    private Guid? Tenant()=>Guid.TryParse(User.FindFirst("tenant_id")?.Value,out var x)?x:null;
    private Guid UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var x)?x:Guid.Empty;
    private static string Escape(object? value)=>$"\"{value?.ToString()?.Replace("\"","\"\"")}\"";
}
