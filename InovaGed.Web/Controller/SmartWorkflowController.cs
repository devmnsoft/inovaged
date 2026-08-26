using InovaGed.Application.Identity;
using InovaGed.Application.SmartWorkflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace InovaGed.Web.Controllers;
[Authorize]
[Route("SmartWorkflow")]
public sealed class SmartWorkflowController(ISmartWorkflowTaskService tasks,ISmartWorkflowRuleEngine rules,ICurrentUser user):Controller
{
 [HttpGet("")] public async Task<IActionResult> Index(CancellationToken ct)=>View(await tasks.GetDashboardAsync(user.TenantId,ct));
 [HttpGet("Tasks")] public async Task<IActionResult> Tasks(string? status,string? priority,string? taskType,string? sourceType,bool overdueOnly,CancellationToken ct)=>View(await tasks.ListAsync(new(user.TenantId,status,priority,taskType,sourceType,overdueOnly),ct));
 [HttpGet("Task/{id:guid}")] public async Task<IActionResult> TaskDetails(Guid id,CancellationToken ct){var x=await tasks.GetAsync(user.TenantId,id,ct);return x is null?NotFound():View(x);}
 [HttpPost("Generate")][ValidateAntiForgeryToken] public async Task<IActionResult> Generate(CancellationToken ct){var n=await rules.GenerateTasksAsync(user.TenantId,user.UserId,ct);TempData["Success"]=$"{n} nova(s) tarefa(s) gerada(s).";return RedirectToAction(nameof(Tasks));}
 [HttpPost("Task/{id:guid}/Assign")][ValidateAntiForgeryToken] public async Task<IActionResult> Assign(Guid id,Guid? assignedTo,CancellationToken ct){await tasks.AssignAsync(user.TenantId,id,assignedTo??user.UserId,user.UserId,ct);return RedirectToAction(nameof(TaskDetails),new{id});}
 [HttpPost("Task/{id:guid}/Start")][ValidateAntiForgeryToken] public async Task<IActionResult> Start(Guid id,CancellationToken ct){await tasks.StartAsync(user.TenantId,id,user.UserId,ct);return RedirectToAction(nameof(TaskDetails),new{id});}
 [HttpPost("Task/{id:guid}/Complete")][ValidateAntiForgeryToken] public async Task<IActionResult> Complete(Guid id,string notes,CancellationToken ct){await tasks.CompleteAsync(user.TenantId,id,user.UserId,notes,ct);return RedirectToAction(nameof(TaskDetails),new{id});}
 [HttpPost("Task/{id:guid}/Cancel")][ValidateAntiForgeryToken] public async Task<IActionResult> Cancel(Guid id,string reason,CancellationToken ct){await tasks.CancelAsync(user.TenantId,id,user.UserId,reason,ct);return RedirectToAction(nameof(TaskDetails),new{id});}
 [HttpGet("Rules")] public async Task<IActionResult> Rules(CancellationToken ct)=>View(await tasks.ListRulesAsync(user.TenantId,ct));
 [HttpGet("Report")] public async Task<IActionResult> Report(CancellationToken ct)=>View(await tasks.GetDashboardAsync(user.TenantId,ct));
}
