using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy=AppPolicies.FullAdminOnly)]
public sealed class LabelTemplatesController(IDbConnectionFactory dbFactory,ILabelTemplateManager manager,ILabelTemplateVersioningService versions,IAuditWriter audit) : GedControllerBase(dbFactory)
{
 [HttpGet] public async Task<IActionResult> Index(CancellationToken ct)=>View(await manager.ListAsync(TenantId,ct));
 [HttpGet] public IActionResult Create()=>View(NewCommand());
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Create(LabelTemplateEditCommand command,CancellationToken ct)=>await ExecuteEdit(command,async()=>{var id=await manager.CreateCustomAsync(TenantId,command,ct); await Audit("CREATE","LABEL_TEMPLATE_CREATED",id,"Modelo personalizado criado",command,ct); TempData["Success"]="Modelo criado com sucesso."; return RedirectToAction(nameof(Edit),new{id});});
 [HttpGet] public async Task<IActionResult> Edit(Guid id,CancellationToken ct) { var d=await manager.GetAsync(TenantId,id,ct); if(d is null)return NotFound(); if(d.Template.IsSystemTemplate){ViewBag.ReadOnly=true;} ViewBag.Template=d.Template; return View(ToCommand(d)); }
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Edit(Guid id,LabelTemplateEditCommand command,CancellationToken ct)=>await ExecuteEdit(command,async()=>{await manager.UpdateAsync(TenantId,id,command,ct); await Audit("UPDATE","LABEL_TEMPLATE_UPDATED",id,"Configuração do modelo editada",null,ct); TempData["Success"]="Alterações salvas."; return RedirectToAction(nameof(Edit),new{id});});
 [HttpPost,ValidateAntiForgeryToken] public Task<IActionResult> Activate(Guid id,CancellationToken ct)=>Mutation(id,"ACTIVATE","LABEL_TEMPLATE_ACTIVATED","Modelo ativado",()=>manager.ActivateAsync(TenantId,id,ct),ct);
 [HttpPost,ValidateAntiForgeryToken] public Task<IActionResult> Deactivate(Guid id,CancellationToken ct)=>Mutation(id,"DEACTIVATE","LABEL_TEMPLATE_DEACTIVATED","Modelo desativado",()=>manager.DeactivateAsync(TenantId,id,ct),ct);
 [HttpPost,ValidateAntiForgeryToken] public Task<IActionResult> SetDefault(Guid id,CancellationToken ct)=>Mutation(id,"UPDATE","LABEL_TEMPLATE_DEFAULT_SET","Modelo definido como padrão",()=>manager.SetDefaultAsync(TenantId,id,ct),ct);
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Publish(Guid id,string? notes,CancellationToken ct) { if(UserId is not Guid uid)return Unauthorized(); try { var version=await versions.PublishVersionAsync(TenantId,id,uid,notes,ct); await Audit("PUBLISH","LABEL_TEMPLATE_VERSION_PUBLISHED",id,$"Versão {version} publicada",new{version,notes},ct); TempData["Success"]=$"Versão {version} publicada com sucesso."; } catch(Exception ex) when(ex is InvalidOperationException or KeyNotFoundException){TempData["Error"]=ex.Message;} return RedirectToAction(nameof(Versions),new{id}); }
 [HttpGet] public async Task<IActionResult> Versions(Guid id,CancellationToken ct) { var template=await manager.GetAsync(TenantId,id,ct); if(template is null)return NotFound(); ViewBag.Template=template.Template; return View(await versions.ListVersionsAsync(TenantId,id,ct)); }
 [HttpGet] public async Task<IActionResult> Preview(Guid id,CancellationToken ct) { var template=await manager.GetAsync(TenantId,id,ct); return template is null?NotFound():View(template); }
 private async Task<IActionResult> Mutation(Guid id,string action,string entity,string message,Func<Task> operation,CancellationToken ct){try{await operation();await Audit(action,entity,id,message,null,ct);TempData["Success"]=message+".";}catch(Exception ex)when(ex is InvalidOperationException or KeyNotFoundException){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index));}
 private async Task<IActionResult> ExecuteEdit(LabelTemplateEditCommand command,Func<Task<IActionResult>> save){if(!ModelState.IsValid)return View(command);try{return await save();}catch(Exception ex)when(ex is InvalidOperationException or KeyNotFoundException){ModelState.AddModelError("",ex.Message);return View(command);}}
 private async Task Audit(string action,string entity,Guid id,string summary,object? data,CancellationToken ct)=>_ = await audit.WriteAsync(TenantId,UserId,action,entity,id,summary,HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),data,ct);
 private static LabelTemplateEditCommand ToCommand(LabelTemplateDetails d)=>new(){Name=d.Template.Name,Description=d.Template.Description,Code=d.Template.Code,SubjectType=d.Template.SubjectType,IsActive=d.Template.IsActive,SupportsBatch=d.Template.SupportsBatch,AllowsManualFields=d.Template.AllowsManualFields,Config=d.Config,Fields=d.Fields.ToList()};
 private static LabelTemplateEditCommand NewCommand()=>new(){Code="CUSTOM_",Config=new(){HeaderText="ARQUIVO LOCDESCK ANANINDEUA"},Fields=FieldDefaults()};
 private static List<LabelTemplateFieldItem> FieldDefaults()=>new[]{("control_number","N° de Controle"),("volume","Volume"),("subject","Assunto"),("details","Detalhamento"),("activity","Atividade"),("classification","Classificação"),("support","Suporte"),("document_period","Período do Documento"),("current_phase","Fase Atual"),("disposal_forecast","Previsão Eliminação"),("disposal_status","Situação Eliminação"),("led_number","Nº LED"),("location","LOCALIZAÇÃO")}.Select((x,i)=>new LabelTemplateFieldItem{FieldKey=x.Item1,FieldLabel=x.Item2,DisplayOrder=(i+1)*10}).ToList();
}
