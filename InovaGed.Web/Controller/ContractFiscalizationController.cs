using System.Text;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Contracts.Fiscalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize,Route("ContractFiscalization")]
public sealed class ContractFiscalizationController(IDbConnectionFactory db,IContractFiscalizationService fiscalization,IContractGlosaService glosas,IContractFiscalizationReportService reports):GedControllerBase(db)
{
 [HttpGet("")] public async Task<IActionResult>Index(DateOnly? competence,string?status,CancellationToken ct)=>View(await fiscalization.ListPeriodsAsync(TenantId,new(competence,status),ct));
 [HttpGet("Create")] public IActionResult Create()=>View(new CreateInput(DateOnly.FromDateTime(DateTime.Today),null,null,null,null,null,null));
 [HttpPost("Create"),ValidateAntiForgeryToken] public async Task<IActionResult>Create(CreateInput input,CancellationToken ct){if(UserId is not Guid u)return Forbid();if(!ModelState.IsValid)return View(input);var id=await fiscalization.CreatePeriodAsync(new(TenantId,input.CompetenceMonth,input.ContractNumber,input.ContractorName,input.ContractingUnit,input.FiscalUserId,input.ManagerUserId,u,input.Notes),ct);return RedirectToAction(nameof(Details),new{id});}
 [HttpGet("{id:guid}")] public async Task<IActionResult>Details(Guid id,CancellationToken ct){var model=await fiscalization.GetPeriodAsync(TenantId,id,ct);return model is null?NotFound():View(model);}
 [HttpPost("{id:guid}/ImportProductivity"),ValidateAntiForgeryToken] public async Task<IActionResult>ImportProductivity(Guid id,CancellationToken ct){if(UserId is not Guid u)return Forbid();await fiscalization.ImportProductivityAsync(TenantId,id,u,ct);return RedirectToAction(nameof(Details),new{id});}
 [HttpGet("{id:guid}/Items")] public async Task<IActionResult>Items(Guid id,CancellationToken ct){ViewBag.PeriodId=id;return View(await fiscalization.ListItemsAsync(TenantId,id,ct));}
 [HttpPost("Items/{itemId:guid}/Approve"),ValidateAntiForgeryToken] public async Task<IActionResult>ApproveItem(Guid itemId,Guid periodId,string?notes,CancellationToken ct){if(UserId is not Guid u)return Forbid();await fiscalization.ApproveItemAsync(TenantId,itemId,u,notes,ct);return RedirectToAction(nameof(Details),new{id=periodId});}
 [HttpGet("Items/{itemId:guid}/Glosa")] public IActionResult GlosaForm(Guid itemId,Guid periodId)=>View(new GlosaInput(periodId,itemId,"QUANTITATIVE","",0,0));
 [HttpPost("Items/{itemId:guid}/Glosa"),ValidateAntiForgeryToken] public async Task<IActionResult>Glosa(Guid itemId,GlosaInput input,CancellationToken ct){if(UserId is not Guid u)return Forbid();if(string.IsNullOrWhiteSpace(input.Reason)){ModelState.AddModelError(nameof(input.Reason),"A justificativa é obrigatória.");return View("GlosaForm",input with{ItemId=itemId});}await glosas.CreateGlosaAsync(new(TenantId,itemId,u,input.GlosaType,input.Reason,input.GlossedQuantity,input.GlossedAmount),ct);return RedirectToAction(nameof(Details),new{id=input.PeriodId});}
 [HttpPost("Glosa/{glosaId:guid}/Respond"),ValidateAntiForgeryToken] public async Task<IActionResult>Respond(Guid glosaId,Guid periodId,string response,CancellationToken ct){if(UserId is not Guid u)return Forbid();await glosas.RespondGlosaAsync(TenantId,glosaId,u,response,ct);return RedirectToAction(nameof(Details),new{id=periodId});}
 [HttpPost("Glosa/{glosaId:guid}/Maintain"),ValidateAntiForgeryToken] public async Task<IActionResult>Maintain(Guid glosaId,Guid periodId,string notes,CancellationToken ct){if(UserId is not Guid u)return Forbid();await glosas.MaintainGlosaAsync(TenantId,glosaId,u,notes,ct);return RedirectToAction(nameof(Details),new{id=periodId});}
 [HttpPost("Glosa/{glosaId:guid}/Revert"),ValidateAntiForgeryToken] public async Task<IActionResult>Revert(Guid glosaId,Guid periodId,string notes,CancellationToken ct){if(UserId is not Guid u)return Forbid();await glosas.RevertGlosaAsync(TenantId,glosaId,u,notes,ct);return RedirectToAction(nameof(Details),new{id=periodId});}
 [HttpPost("{id:guid}/Evidence"),ValidateAntiForgeryToken] public async Task<IActionResult>Evidence(Guid id,EvidenceInput input,CancellationToken ct){if(UserId is not Guid u)return Forbid();await fiscalization.AttachEvidenceAsync(new(TenantId,id,input.ItemId,u,input.EvidenceType,input.Title,input.Description,input.ExternalUrl,input.SourceType,input.SourceId),ct);return RedirectToAction(nameof(Details),new{id});}
 [HttpPost("{id:guid}/Submit"),ValidateAntiForgeryToken] public async Task<IActionResult>Submit(Guid id,CancellationToken ct){if(UserId is not Guid u)return Forbid();await fiscalization.SubmitForApprovalAsync(TenantId,id,u,ct);return RedirectToAction(nameof(Details),new{id});}
 [HttpPost("{id:guid}/Approve"),ValidateAntiForgeryToken] public async Task<IActionResult>Approve(Guid id,string?notes,CancellationToken ct){if(UserId is not Guid u)return Forbid();await fiscalization.ApprovePeriodAsync(TenantId,id,u,notes,ct);return RedirectToAction(nameof(Details),new{id});}
 [HttpPost("{id:guid}/Reject"),ValidateAntiForgeryToken] public async Task<IActionResult>Reject(Guid id,string reason,CancellationToken ct){if(UserId is not Guid u)return Forbid();await fiscalization.RejectPeriodAsync(TenantId,id,u,reason,ct);return RedirectToAction(nameof(Details),new{id});}
 [HttpPost("{id:guid}/Close"),ValidateAntiForgeryToken] public async Task<IActionResult>Close(Guid id,CancellationToken ct){if(UserId is not Guid u)return Forbid();await fiscalization.ClosePeriodAsync(TenantId,id,u,ct);return RedirectToAction(nameof(Details),new{id});}
 [HttpGet("{id:guid}/SyntheticReport")] public async Task<IActionResult>SyntheticReport(Guid id,CancellationToken ct)=>View(await reports.GenerateSyntheticReportAsync(TenantId,id,ct));
 [HttpGet("{id:guid}/AnalyticalReport")] public async Task<IActionResult>AnalyticalReport(Guid id,CancellationToken ct)=>View(await reports.GenerateAnalyticalReportAsync(TenantId,id,ct));
 [HttpGet("{id:guid}/AcceptanceTerm")] public async Task<IActionResult>AcceptanceTerm(Guid id,CancellationToken ct)=>View(await reports.GenerateSyntheticReportAsync(TenantId,id,ct));
 [HttpGet("{id:guid}/DispatchSheet")] public async Task<IActionResult>DispatchSheet(Guid id,CancellationToken ct)=>View(await reports.GenerateSyntheticReportAsync(TenantId,id,ct));
 [HttpGet("{id:guid}/Export")] public async Task<IActionResult>Export(Guid id,string reportType="analytical",CancellationToken ct){var csv=await reports.ExportCsvAsync(TenantId,id,reportType,ct);return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),"text/csv",$"fiscalizacao-{id:N}-{reportType}.csv");}
 public sealed record CreateInput(DateOnly CompetenceMonth,string?ContractNumber,string?ContractorName,string?ContractingUnit,Guid?FiscalUserId,Guid?ManagerUserId,string?Notes);
 public sealed record GlosaInput(Guid PeriodId,Guid ItemId,string GlosaType,string Reason,decimal GlossedQuantity,decimal GlossedAmount);
 public sealed record EvidenceInput(Guid?ItemId,string EvidenceType,string Title,string?Description,string?ExternalUrl,string?SourceType,Guid?SourceId);
}
