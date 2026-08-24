using System.Security.Claims;
using InovaGed.Application.Release.Uat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace InovaGed.Web.Controllers;
[Authorize,Route("UatReadiness")]
public sealed class UatReadinessController(IUatTestPlanService plans,IUatExecutionService runs,IReleaseEvidenceService evidence):Controller
{
 [HttpGet("")] public async Task<IActionResult> Index(CancellationToken ct)=>View(await runs.GetSummaryAsync(Tenant(),ct));
 [HttpGet("Plans")] public async Task<IActionResult> Plans(CancellationToken ct)=>View(await plans.ListPlansAsync(Tenant(),ct));
 [HttpGet("Plan/{id:guid}")] public async Task<IActionResult> Plan(Guid id,CancellationToken ct){var x=await plans.GetDefaultPlanAsync(Tenant(),ct);return x.Plan.Id==id?View("PlanDetails",x):NotFound();}
 [HttpPost("Plan/Create"),ValidateAntiForgeryToken] public async Task<IActionResult> Create(string planCode,string title,string? description,string? releaseVersion,CancellationToken ct){var id=await plans.CreatePlanAsync(new(Tenant(),planCode,title,description,releaseVersion,UserId()),ct);return RedirectToAction(nameof(Plan),new{id});}
 [HttpPost("Run/Start"),ValidateAntiForgeryToken] public async Task<IActionResult> Start(Guid planId,CancellationToken ct){var id=await runs.StartRunAsync(Tenant(),planId,UserId(),ct);return RedirectToAction(nameof(Run),new{id});}
 [HttpGet("Run/{id:guid}")] public async Task<IActionResult> Run(Guid id,CancellationToken ct)=>View("RunDetails",await runs.GetRunAsync(id,ct));
 [HttpPost("Result"),ValidateAntiForgeryToken] public async Task<IActionResult> Result(Guid runId,Guid testCaseId,string result,string? actualResult,string? evidenceNotes,Guid? incidentId,CancellationToken ct){await runs.RecordResultAsync(new(Tenant(),runId,testCaseId,result,actualResult,evidenceNotes,incidentId,UserId()),ct);return RedirectToAction(nameof(Run),new{id=runId});}
 [HttpPost("Evidence"),ValidateAntiForgeryToken] public async Task<IActionResult> Evidence(Guid sourceId,string sourceType,string title,string? description,string evidenceType="TEXT",string? externalUrl=null,CancellationToken ct){await evidence.AddEvidenceAsync(new(Tenant(),sourceType,sourceId,title,description,evidenceType,null,externalUrl,null,UserId()),ct);return RedirectToAction(nameof(Run),new{id=sourceId});}
 [HttpGet("Report/{runId:guid}")] public async Task<IActionResult> Report(Guid runId,CancellationToken ct)=>View(await runs.GetRunAsync(runId,ct));
 [HttpGet("AcceptanceTerm/{runId:guid}")] public async Task<IActionResult> AcceptanceTerm(Guid runId,CancellationToken ct)=>View(await runs.GetRunAsync(runId,ct));
 [HttpGet("GoLiveChecklist")] public IActionResult GoLiveChecklist()=>View();
 private Guid? Tenant()=>Guid.TryParse(User.FindFirst("tenant_id")?.Value,out var x)?x:null;
 private Guid UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var x)?x:Guid.Empty;
}
