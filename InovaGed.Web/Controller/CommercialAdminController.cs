using InovaGed.Web.Services.Commercial;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Roles="admin_valora")]
[Route("admin/comercial")]
public sealed class CommercialAdminController(IPublicCommercialService commercial) : Controller
{
    [HttpGet("leads")] public async Task<IActionResult> Leads(string? status,CancellationToken ct)=>View(await commercial.GetLeadsAsync(status,ct));
    [HttpPost("leads/{id:guid}"),ValidateAntiForgeryToken] public async Task<IActionResult> UpdateLead(Guid id,string status,string? note,CancellationToken ct) { Guid.TryParse(User.FindFirst("sub")?.Value,out var userId); await commercial.UpdateLeadAsync(id,status,note,userId==Guid.Empty?null:userId,ct); return RedirectToAction(nameof(Leads)); }
    [HttpGet("trials")] public IActionResult Trials()=>View();
    [HttpGet("eventos")] public IActionResult Events()=>View();
}
