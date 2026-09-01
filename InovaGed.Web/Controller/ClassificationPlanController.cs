using InovaGed.Application.Classification;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class ClassificationPlanController : Controller
{
    private readonly IClassificationPlanService _plan;
    private readonly IRetentionRuleV2Service _rules;
    private readonly IClassificationVersionService _versions;
    private readonly ICurrentUser _user;
    private readonly ILogger<ClassificationPlanController> _logger;
    public ClassificationPlanController(IClassificationPlanService plan, IRetentionRuleV2Service rules, IClassificationVersionService versions, ICurrentUser user, ILogger<ClassificationPlanController> logger) => (_plan,_rules,_versions,_user,_logger)=(plan,rules,versions,user,logger);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await Safe(() => _plan.GetDashboardAsync(_user.TenantId,ct),new ClassificationPlanDashboard(0,0,0,0,0,0)));
    [HttpGet("Tree")]
    public async Task<IActionResult> Tree(Guid? selected,CancellationToken ct){var tree=await Safe(()=>_plan.GetTreeAsync(_user.TenantId,ct),Array.Empty<ClassificationTreeNode>());ViewBag.Selected=selected;return View(tree);}
    [HttpGet("Class/{id:guid}")]
    public async Task<IActionResult> Class(Guid id,CancellationToken ct){var item=await _plan.GetNodeAsync(_user.TenantId,id,ct);return item is null?NotFound():Json(item);}
    [HttpGet("Create")]
    public async Task<IActionResult> Create(Guid? parentId,CancellationToken ct){ViewBag.Nodes=await _plan.GetTreeAsync(_user.TenantId,ct);return View("Edit",new NodeForm{ParentId=parentId});}
    [HttpPost("Create")][ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NodeForm form,CancellationToken ct){if(!ModelState.IsValid){ViewBag.Nodes=await _plan.GetTreeAsync(_user.TenantId,ct);return View("Edit",form);}try{await _plan.CreateNodeAsync(form.Create(_user.TenantId,_user.UserId),ct);TempData["Success"]="Classe criada com auditoria.";return RedirectToAction(nameof(Tree));}catch(Exception ex){ModelState.AddModelError("",ex.Message);ViewBag.Nodes=await _plan.GetTreeAsync(_user.TenantId,ct);return View("Edit",form);}}
    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id,CancellationToken ct){var n=await _plan.GetNodeAsync(_user.TenantId,id,ct);if(n is null)return NotFound();ViewBag.Nodes=(await _plan.GetTreeAsync(_user.TenantId,ct)).Where(x=>x.Id!=id).ToList();return View(NodeForm.From(n));}
    [HttpPost("Edit/{id:guid}")][ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id,NodeForm form,CancellationToken ct){form.Id=id;try{await _plan.UpdateNodeAsync(form.Update(_user.TenantId,_user.UserId),ct);TempData["Success"]="Classe atualizada.";return RedirectToAction(nameof(Tree),new{selected=id});}catch(Exception ex){ModelState.AddModelError("",ex.Message);ViewBag.Nodes=(await _plan.GetTreeAsync(_user.TenantId,ct)).Where(x=>x.Id!=id).ToList();return View(form);}}
    [HttpGet("RetentionRules")]
    public async Task<IActionResult> RetentionRules(string? q,string? status,CancellationToken ct)=>View(await Safe(()=>_rules.ListAsync(_user.TenantId,new(q,status),ct),Array.Empty<RetentionRuleListItem>()));
    [HttpGet("RetentionRule/{id:guid}")]
    public async Task<IActionResult> RetentionRule(Guid id,CancellationToken ct){ViewBag.Nodes=await _plan.GetTreeAsync(_user.TenantId,ct);var rule=await _rules.GetByClassificationAsync(_user.TenantId,id,ct);return View("RetentionRuleDetails",RuleForm.From(id,rule));}
    [HttpPost("RetentionRule/Save")][ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRetentionRule(RuleForm form,CancellationToken ct){try{await _rules.SaveAsync(form.Command(_user.TenantId,_user.UserId),ct);TempData["Success"]="Regra de temporalidade salva.";return RedirectToAction(nameof(RetentionRules));}catch(Exception ex){ModelState.AddModelError("",ex.Message);ViewBag.Nodes=await _plan.GetTreeAsync(_user.TenantId,ct);return View("RetentionRuleDetails",form);}}
    [HttpGet("Versions")]
    public async Task<IActionResult> Versions(CancellationToken ct)=>View(await Safe(()=>_versions.ListVersionsAsync(_user.TenantId,ct),Array.Empty<ClassificationVersionItem>()));
    [HttpGet("Versions/{id:guid}")]
    public async Task<IActionResult> Version(Guid id,CancellationToken ct){var v=(await _versions.ListVersionsAsync(_user.TenantId,ct)).FirstOrDefault(x=>x.Id==id);return v is null?NotFound():Json(v);}
    [HttpPost("PublishVersion")][ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishVersion(string? notes,CancellationToken ct){await _versions.PublishAsync(_user.TenantId,_user.UserId,notes??"",ct);TempData["Success"]="Nova versão publicada.";return RedirectToAction(nameof(Versions));}
    [HttpGet("Compare")]
    public async Task<IActionResult> Compare(CancellationToken ct){ViewBag.Versions=await _versions.ListVersionsAsync(_user.TenantId,ct);return View(new ClassificationVersionCompareResult(Guid.Empty,Guid.Empty,Array.Empty<ClassificationVersionDifference>()));}
    [HttpPost("Compare")][ValidateAntiForgeryToken]
    public async Task<IActionResult> Compare(Guid fromVersionId,Guid toVersionId,CancellationToken ct){ViewBag.Versions=await _versions.ListVersionsAsync(_user.TenantId,ct);return View(await _versions.CompareAsync(_user.TenantId,fromVersionId,toVersionId,ct));}
    [HttpGet("Import")]
    public IActionResult Import()=>View(Array.Empty<ImportPreviewRow>());
    [HttpPost("Import")][ValidateAntiForgeryToken][RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> Import(IFormFile? file,bool confirm,CancellationToken ct){var rows=new List<ImportPreviewRow>();if(file is null||file.Length==0){ModelState.AddModelError("","Selecione um CSV.");return View(rows);}using var reader=new StreamReader(file.OpenReadStream());var header=(await reader.ReadLineAsync(ct))?.Split(',')??[];string? line;var number=1;while((line=await reader.ReadLineAsync(ct)) is not null){number++;var v=line.Split(',');var code=At("code");var title=At("title");var parent=At("parent_code");rows.Add(new(number,code,title,parent,string.IsNullOrWhiteSpace(code)||string.IsNullOrWhiteSpace(title)?"Código e título são obrigatórios.":null));string At(string key){var i=Array.FindIndex(header,h=>h.Trim().Equals(key,StringComparison.OrdinalIgnoreCase));return i>=0&&i<v.Length?v[i].Trim():"";}}if(confirm&&rows.All(x=>x.Error is null)){var tree=(await _plan.GetTreeAsync(_user.TenantId,ct)).ToDictionary(x=>x.Code,StringComparer.OrdinalIgnoreCase);foreach(var row in rows){Guid? parent=string.IsNullOrEmpty(row.ParentCode)?null:tree.GetValueOrDefault(row.ParentCode)?.Id;var id=await _plan.CreateNodeAsync(new(_user.TenantId,_user.UserId,parent,row.Code,row.Title,null,"MEIO",null,null,null,tree.Count,"DRAFT",true),ct);tree[row.Code]=new(id,parent,row.Code,row.Title,"MEIO","DRAFT",true,false);}TempData["Success"]=$"{rows.Count} classes importadas.";return RedirectToAction(nameof(Tree));}ViewBag.FileName=file.FileName;return View(rows);}
    [HttpGet("ReviewQueue")]
    public async Task<IActionResult> ReviewQueue(CancellationToken ct){var nodes=await _plan.GetTreeAsync(_user.TenantId,ct);var rules=await _rules.ListAsync(_user.TenantId,new(null,"DRAFT"),ct);return View(new ReviewQueueVm(nodes.Where(x=>x.ReviewStatus=="DRAFT").ToList(),rules));}
    private async Task<T> Safe<T>(Func<Task<T>> action,T fallback){try{return await action();}catch(Exception ex){_logger.LogError(ex,"Falha segura no Plano de Classificação 2.0");TempData["Error"]="Estrutura 2.0 indisponível. Aplique a migration obrigatória.";return fallback;}}
    public sealed class NodeForm{public Guid? Id{get;set;}public Guid? ParentId{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public string? Description{get;set;}public string ActivityType{get;set;}="MEIO";public string? DocumentFunction{get;set;}public string? NormativeSource{get;set;}public string? Keywords{get;set;}public int DisplayOrder{get;set;}public string ReviewStatus{get;set;}="DRAFT";public bool IsActive{get;set;}=true;public ClassificationNodeCreateCommand Create(Guid t,Guid u)=>new(t,u,ParentId,Code,Title,Description,ActivityType,DocumentFunction,NormativeSource,Keywords,DisplayOrder,ReviewStatus,IsActive);public ClassificationNodeUpdateCommand Update(Guid t,Guid u)=>new(t,u,Id!.Value,ParentId,Code,Title,Description,ActivityType,DocumentFunction,NormativeSource,Keywords,DisplayOrder,ReviewStatus,IsActive);public static NodeForm From(ClassificationNodeDetails n)=>new(){Id=n.Id,ParentId=n.ParentId,Code=n.Code,Title=n.Title,Description=n.Description,ActivityType=n.ActivityType??"MEIO",DocumentFunction=n.DocumentFunction,NormativeSource=n.NormativeSource,Keywords=n.Keywords,DisplayOrder=n.DisplayOrder,ReviewStatus=n.ReviewStatus,IsActive=n.IsActive};}
    public sealed class RuleForm{public Guid? Id{get;set;}public Guid ClassificationNodeId{get;set;}public int? CurrentPhaseYears{get;set;}public int? IntermediatePhaseYears{get;set;}public string FinalDestination{get;set;}="REVISAO";public string? TriggerEvent{get;set;}public string? TriggerDescription{get;set;}public string? LegalBasis{get;set;}public string? Observation{get;set;}public string ReviewStatus{get;set;}="DRAFT";public DateOnly? EffectiveFrom{get;set;}public DateOnly? EffectiveTo{get;set;}public RetentionRuleSaveCommand Command(Guid t,Guid u)=>new(t,u,Id,ClassificationNodeId,CurrentPhaseYears,IntermediatePhaseYears,FinalDestination,TriggerEvent,TriggerDescription,LegalBasis,Observation,ReviewStatus,EffectiveFrom,EffectiveTo);public static RuleForm From(Guid id,RetentionRuleDetails? r)=>r is null?new(){ClassificationNodeId=id}:new(){Id=r.Id,ClassificationNodeId=r.ClassificationNodeId,CurrentPhaseYears=r.CurrentPhaseYears,IntermediatePhaseYears=r.IntermediatePhaseYears,FinalDestination=r.FinalDestination,TriggerEvent=r.TriggerEvent,TriggerDescription=r.TriggerDescription,LegalBasis=r.LegalBasis,Observation=r.Observation,ReviewStatus=r.ReviewStatus,EffectiveFrom=r.EffectiveFrom,EffectiveTo=r.EffectiveTo};}
    public sealed record ImportPreviewRow(int Line,string Code,string Title,string ParentCode,string? Error);
    public sealed record ReviewQueueVm(IReadOnlyList<ClassificationTreeNode> DraftClasses,IReadOnlyList<RetentionRuleListItem> DraftRules);
}
