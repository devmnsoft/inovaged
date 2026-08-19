using System.Security.Claims;
using InovaGed.Application.Contracts.FiscalPortal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[AllowAnonymous,Route("FiscalPortal")]
public sealed class FiscalPortalController(IFiscalPortalPublicationService publications,IFiscalPortalManifestationService manifestations,IFiscalAcceptanceSignatureService signatures):Controller
{
 const string Cookie="InovaGed.FiscalPortal";
 [HttpGet("Access/{token}")] public async Task<IActionResult>Access(string token,CancellationToken ct){var x=await publications.GetByTokenAsync(token,ct);if(x is null)return View("Access",model:"Este link é inválido, expirou ou foi revogado.");Response.Cookies.Append(Cookie,token,new(){HttpOnly=true,Secure=Request.IsHttps,SameSite=SameSiteMode.Lax,Expires=x.Publication.ExpiresAt??DateTimeOffset.UtcNow.AddHours(8),IsEssential=true});await publications.MarkViewedAsync(x.Publication.TenantId,x.Publication.Id,Ip(),Agent(),ct);return RedirectToAction(nameof(Dashboard),new{publicationId=x.Publication.Id});}
 [HttpGet("{publicationId:guid}")] public async Task<IActionResult>Dashboard(Guid publicationId,CancellationToken ct){var x=await Authorized(publicationId,ct);return x is null?Unauthorized():View(x);}
 [HttpGet("{publicationId:guid}/SyntheticReport")] public Task<IActionResult> SyntheticReport(Guid publicationId,CancellationToken ct)=>PortalView(publicationId,"SyntheticReport",ct);
 [HttpGet("{publicationId:guid}/AnalyticalReport")] public Task<IActionResult> AnalyticalReport(Guid publicationId,CancellationToken ct)=>PortalView(publicationId,"AnalyticalReport",ct);
 [HttpGet("{publicationId:guid}/Evidences")] public Task<IActionResult> Evidences(Guid publicationId,CancellationToken ct)=>PortalView(publicationId,"Evidences",ct);
 [HttpGet("{publicationId:guid}/Glosas")] public Task<IActionResult> Glosas(Guid publicationId,CancellationToken ct)=>PortalView(publicationId,"Glosas",ct);
 [HttpGet("{publicationId:guid}/Accept")] public Task<IActionResult> Accept(Guid publicationId,CancellationToken ct)=>PortalView(publicationId,"Accept",ct);
 [HttpGet("{publicationId:guid}/Reject")] public Task<IActionResult> Reject(Guid publicationId,CancellationToken ct)=>PortalView(publicationId,"Reject",ct);
 [HttpGet("{publicationId:guid}/Receipt")] public Task<IActionResult> Receipt(Guid publicationId,CancellationToken ct)=>PortalView(publicationId,"Receipt",ct);
 [HttpPost("{publicationId:guid}/Accept"),ValidateAntiForgeryToken] public async Task<IActionResult>Accept(Guid publicationId,string signerName,string?signerEmail,string?signerDocument,bool declarationAccepted,string?notes,CancellationToken ct){var x=await Authorized(publicationId,ct);if(x is null)return Unauthorized();await signatures.SignElectronicallyAsync(new(x.Publication.TenantId,publicationId,UserId(),signerName,signerEmail,signerDocument,declarationAccepted,notes,Ip(),Agent()),ct);return RedirectToAction(nameof(Receipt),new{publicationId});}
 [HttpPost("{publicationId:guid}/SignElectronic"),ValidateAntiForgeryToken] public Task<IActionResult>SignElectronic(Guid publicationId,string signerName,string?signerEmail,string?signerDocument,bool declarationAccepted,string?notes,CancellationToken ct)=>Accept(publicationId,signerName,signerEmail,signerDocument,declarationAccepted,notes,ct);
 [HttpPost("{publicationId:guid}/Reject"),ValidateAntiForgeryToken] public async Task<IActionResult>Reject(Guid publicationId,string reason,string details,string?relatedItem,string?name,string?email,CancellationToken ct){var x=await Authorized(publicationId,ct);if(x is null)return Unauthorized();await manifestations.RejectAsync(new(x.Publication.TenantId,publicationId,UserId(),reason,details,relatedItem,name,email,Ip(),Agent()),ct);return RedirectToAction(nameof(Dashboard),new{publicationId});}
 [HttpPost("{publicationId:guid}/RequestCorrection"),ValidateAntiForgeryToken] public async Task<IActionResult>RequestCorrection(Guid publicationId,string reason,string details,string?relatedItem,string?name,string?email,CancellationToken ct){var x=await Authorized(publicationId,ct);if(x is null)return Unauthorized();await manifestations.RequestCorrectionAsync(new(x.Publication.TenantId,publicationId,UserId(),reason,details,relatedItem,name,email,Ip(),Agent()),ct);return RedirectToAction(nameof(Dashboard),new{publicationId});}
 async Task<IActionResult>PortalView(Guid id,string view,CancellationToken ct){var x=await Authorized(id,ct);return x is null?Unauthorized():View(view,x);}
 async Task<FiscalPortalPublicationDetails?>Authorized(Guid id,CancellationToken ct){if(Request.Cookies.TryGetValue(Cookie,out var token)){var x=await publications.GetByTokenAsync(token,ct);if(x?.Publication.Id==id)return x;}if(User.Identity?.IsAuthenticated==true&&Guid.TryParse(User.FindFirst("tenant_id")?.Value,out var tenant)){var x=await publications.GetAsync(tenant,id,ct);if(x is not null&&x.Publication.Status is not "DRAFT" and not "REVOKED" and not "EXPIRED"&&(x.Publication.ExpiresAt is null||x.Publication.ExpiresAt>DateTimeOffset.UtcNow))return x;}return null;}
 Guid?UserId()=>Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value,out var id)?id:null;string?Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();string?Agent()=>Request.Headers.UserAgent.ToString();
}
