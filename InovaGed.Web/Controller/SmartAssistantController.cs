using InovaGed.Application.Identity;
using InovaGed.Application.SmartGed.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace InovaGed.Web.Controllers;
[Authorize]
[Route("SmartAssistant")]
public sealed class SmartAssistantController(ISmartGedAssistantService assistant,ICurrentUser user):Controller
{
 [HttpGet("")] public IActionResult Index()=>View();
 [HttpGet("Sessions")] public async Task<IActionResult> Sessions(CancellationToken ct)=>View(await assistant.ListSessionsAsync(user.TenantId,user.UserId,ct));
 [HttpGet("Session/{id:guid}")] public async Task<IActionResult> Session(Guid id,CancellationToken ct){var model=await assistant.GetSessionAsync(user.TenantId,id,ct);return model is null?NotFound():View(model);}
 [HttpPost("Start")][ValidateAntiForgeryToken] public async Task<IActionResult> Start(string? title,CancellationToken ct){var session=await assistant.StartSessionAsync(user.TenantId,user.UserId,title,ct);return RedirectToAction(nameof(Session),new{id=session.Id});}
 [HttpPost("Ask")][ValidateAntiForgeryToken] public async Task<IActionResult> Ask(Guid sessionId,string question,CancellationToken ct){await assistant.AskAsync(new(user.TenantId,user.UserId,sessionId,question),ct);return RedirectToAction(nameof(Session),new{id=sessionId});}
 [HttpGet("Citations/{messageId:guid}")] public async Task<IActionResult> Citations(Guid messageId,CancellationToken ct)=>PartialView("_Citations",await assistant.GetCitationsAsync(user.TenantId,messageId,ct));
 [HttpPost("ActionSuggestion/{id:guid}/Accept")][ValidateAntiForgeryToken] public Task<IActionResult> Accept(Guid id,Guid sessionId,string? notes,CancellationToken ct)=>Review(id,sessionId,true,notes,ct);
 [HttpPost("ActionSuggestion/{id:guid}/Reject")][ValidateAntiForgeryToken] public Task<IActionResult> Reject(Guid id,Guid sessionId,string? notes,CancellationToken ct)=>Review(id,sessionId,false,notes,ct);
 private async Task<IActionResult> Review(Guid id,Guid sessionId,bool accept,string? notes,CancellationToken ct){await assistant.ReviewActionAsync(user.TenantId,id,user.UserId,accept,notes,ct);return RedirectToAction(nameof(Session),new{id=sessionId});}
}
