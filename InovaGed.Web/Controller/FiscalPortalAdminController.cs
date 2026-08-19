using System.Security.Cryptography;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Contracts.FiscalPortal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize,Route("FiscalPortalAdmin")]
public sealed class FiscalPortalAdminController(IDbConnectionFactory db,IFiscalPortalPublicationService publications):GedControllerBase(db)
{
 [HttpGet("")] public IActionResult Index()=>View();
 [HttpGet("Publications")] public async Task<IActionResult>Publications(Guid? fiscalizationPeriodId,string?status,CancellationToken ct)=>View(await publications.ListAsync(TenantId,new(fiscalizationPeriodId,status),ct));
 [HttpGet("Create/{fiscalizationPeriodId:guid}")] public IActionResult Create(Guid fiscalizationPeriodId)=>View(new CreateInput(fiscalizationPeriodId,"TOKEN",null,null,null,DateTimeOffset.UtcNow.AddDays(7)));
 [HttpPost("Create/{fiscalizationPeriodId:guid}"),ValidateAntiForgeryToken] public async Task<IActionResult>Create(Guid fiscalizationPeriodId,CreateInput input,CancellationToken ct){if(UserId is not Guid u)return Forbid();var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();var id=await publications.CreatePublicationAsync(new(TenantId,fiscalizationPeriodId,input.AccessMode,input.AccessMode=="TOKEN"?token:null,input.ExpiresAt,u,input.FiscalName,input.FiscalEmail,input.Notes),ct);TempData["PortalToken"]=token;return RedirectToAction(nameof(Details),new{publicationId=id});}
 [HttpPost("Publish/{publicationId:guid}"),ValidateAntiForgeryToken] public async Task<IActionResult>Publish(Guid publicationId,CancellationToken ct){if(UserId is not Guid u)return Forbid();await publications.PublishAsync(TenantId,publicationId,u,ct);return RedirectToAction(nameof(Details),new{publicationId});}
 [HttpPost("Revoke/{publicationId:guid}"),ValidateAntiForgeryToken] public async Task<IActionResult>Revoke(Guid publicationId,string reason,CancellationToken ct){if(UserId is not Guid u)return Forbid();await publications.RevokeAsync(TenantId,publicationId,u,reason,ct);return RedirectToAction(nameof(Details),new{publicationId});}
 [HttpGet("Details/{publicationId:guid}")] public async Task<IActionResult>Details(Guid publicationId,CancellationToken ct){var x=await publications.GetAsync(TenantId,publicationId,ct);return x is null?NotFound():View(x);}
 [HttpGet("Events/{publicationId:guid}")] public async Task<IActionResult>Events(Guid publicationId,CancellationToken ct){var x=await publications.GetAsync(TenantId,publicationId,ct);return x is null?NotFound():View(x);}
 public sealed record CreateInput(Guid FiscalizationPeriodId,string AccessMode,string?FiscalName,string?FiscalEmail,string?Notes,DateTimeOffset?ExpiresAt);
}
