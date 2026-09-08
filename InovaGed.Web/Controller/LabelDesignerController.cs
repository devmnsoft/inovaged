using System.Text.Json;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Web.Models.Labels;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.LabelDesignerRead)]
public sealed class LabelDesignerController(IDbConnectionFactory dbFactory, ILabelCanvasDesignService designs,
    ILabelCanvasRenderService renderer, ILabelCanvasFieldCatalogService fieldCatalog,
    ILogger<LabelDesignerController> logger) : GedControllerBase(dbFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [HttpGet("/Labels/Designer")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try { return View("~/Views/Labels/Designer/Index.cshtml", await designs.ListAsync(TenantId, ct)); }
        catch (Exception exception) { logger.LogError(exception,"Não foi possível listar templates canvas."); ViewBag.SchemaPending=true; return View("~/Views/Labels/Designer/Index.cshtml",Array.Empty<LabelCanvasDesignDto>()); }
    }

    [HttpGet("/Labels/Designer/New")]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public IActionResult New()
    {
        var document=NewDocument("Document","Documento GED",100,70);
        var design=new LabelCanvasDesignDto { Id=Guid.Empty,TenantId=TenantId,TemplateName="Novo modelo de etiqueta",TemplateKind="GED",SubjectType="Document",PaperKind="A4",WidthMm=100,HeightMm=70,Orientation="portrait",Status="DRAFT",DesignJson=JsonSerializer.Serialize(document,JsonOptions),CurrentVersion=1,CreatedAt=DateTime.UtcNow };
        return View("~/Views/Labels/Designer/Edit.cshtml",Page(design,true));
    }

    [HttpPost("/Labels/Designer/New"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> New([FromBody] LabelCanvasSaveRequest request,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();
        return await ExecuteWrite(async()=>{var created=await designs.CreateDraftAsync(TenantId,userId,request,Ip(),Agent(),ct);return Ok(new{ok=true,message="Rascunho criado.",templateKey=created.TemplateKey,redirectUrl=Url.Action(nameof(Edit),new{templateKey=created.TemplateKey})});},"criar",request.TemplateKey);
    }

    [HttpGet("/Labels/Designer/Edit/{templateKey}")]
    [HttpGet("/Labels/Designer/{templateKey}/Edit")]
    public async Task<IActionResult> Edit(string templateKey,CancellationToken ct)
    { var design=await designs.GetAsync(TenantId,templateKey,ct); return design is null?NotFound():View("~/Views/Labels/Designer/Edit.cshtml",Page(design)); }

    [HttpPost("/Labels/Designer/Edit/{templateKey}")]
    [HttpPost("/Labels/Designer/{templateKey}/Save")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerUpdate)]
    public async Task<IActionResult> Save(string templateKey,[FromBody] LabelCanvasSaveRequest request,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();request=CopyWithKey(request,templateKey);
        return await ExecuteWrite(async()=>{var saved=await designs.SaveDraftAsync(TenantId,userId,request,Ip(),Agent(),ct);return Ok(new{ok=true,message="Rascunho salvo.",updatedAt=saved.UpdatedAt});},"salvar",templateKey);
    }

    [HttpGet("/Labels/Designer/{templateKey}")]
    public async Task<IActionResult> Details(string templateKey,CancellationToken ct)
    { var design=await designs.GetAsync(TenantId,templateKey,ct); return design is null?NotFound():View("~/Views/Labels/Designer/Details.cshtml",Page(design)); }

    [HttpPost("/Labels/Designer/Validate/{templateKey}")]
    [HttpPost("/Labels/Designer/{templateKey}/Validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateDesign(string templateKey,[FromBody] LabelCanvasSaveRequest? request,CancellationToken ct)
    {
        var design=await designs.GetAsync(TenantId,templateKey,ct);if(design is null)return NotFound();
        var json=request?.DesignJson??design.DesignJson;var allowed=fieldCatalog.GetFields(request?.SubjectType??design.SubjectType).Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);var validation=renderer.Validate(json,allowed);
        await designs.RecordEventAsync(TenantId,UserId,design.Id,validation.HasErrors?"VALIDATION_FAILED":"VALIDATION_PASSED",validation.HasErrors?"A validação encontrou erros.":"Layout validado.",validation,Ip(),Agent(),ct);
        return Ok(validation);
    }

    [HttpGet("/Labels/Designer/Preview/{templateKey}")]
    [HttpGet("/Labels/Designer/{templateKey}/Preview")]
    [Authorize(Policy=AppPolicies.LabelDesignerPreview)]
    public async Task<IActionResult> Preview(string templateKey,string? profile,CancellationToken ct)
    {
        var design=await designs.GetAsync(TenantId,templateKey,ct);if(design is null)return NotFound();var selected=profile??SampleProfile(design);var rendered=renderer.Render(design,fieldCatalog.GetSampleData(selected));
        await designs.RecordEventAsync(TenantId,UserId,design.Id,"PREVIEW_TEMPLATE","Preview gerado.",new{profile=selected,rendered.SnapshotHash},Ip(),Agent(),ct);
        return View("~/Views/Labels/Designer/Preview.cshtml",new LabelCanvasDesignerPageViewModel(design,fieldCatalog.GetFields(design.SubjectType),rendered.Validation,rendered.Html));
    }

    [HttpGet("/Labels/Designer/TestPrint/{templateKey}")]
    [HttpGet("/Labels/Designer/{templateKey}/PrintTest")]
    [Authorize(Policy=AppPolicies.LabelDesignerPrintTest)]
    public async Task<IActionResult> TestPrint(string templateKey,string? profile,CancellationToken ct)
    {
        var design=await designs.GetAsync(TenantId,templateKey,ct);if(design is null)return NotFound();var rendered=renderer.Render(design,fieldCatalog.GetSampleData(profile??SampleProfile(design)),true);
        await designs.RecordEventAsync(TenantId,UserId,design.Id,"TEST_PRINT","Impressão de teste gerada.",new{rendered.SnapshotHash},Ip(),Agent(),ct);return Content(rendered.Html,"text/html; charset=utf-8");
    }

    [HttpGet("/Labels/Designer/Publish/{templateKey}")]
    public Task<IActionResult> Publish(string templateKey,CancellationToken ct)=>Details(templateKey,ct);

    [HttpPost("/Labels/Designer/Publish/{templateKey}")]
    [HttpPost("/Labels/Designer/{templateKey}/Publish")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerPublish)]
    public async Task<IActionResult> Publish(string templateKey,[FromBody] LabelCanvasPublishRequest? request,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();request??=new LabelCanvasPublishRequest();var normalized=new LabelCanvasPublishRequest{TemplateKey=templateKey,ChangeSummary=request.ChangeSummary,ConfirmWarnings=request.ConfirmWarnings};
        return await ExecuteWrite(async()=>{var published=await designs.PublishAsync(TenantId,userId,normalized,Ip(),Agent(),ct);return Ok(new{ok=true,message=$"Versão {published.CurrentVersion} publicada.",redirectUrl=Url.Action(nameof(Details),new{templateKey})});},"publicar",templateKey);
    }

    [HttpGet("/Labels/Designer/Duplicate/{templateKey}")]
    public Task<IActionResult> Duplicate(string templateKey,CancellationToken ct)=>Details(templateKey,ct);

    [HttpPost("/Labels/Designer/Duplicate/{templateKey}")]
    [HttpPost("/Labels/Designer/{templateKey}/Duplicate")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> Duplicate(string templateKey,[FromBody] JsonElement? payload,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();string? name=null;if(payload is {ValueKind:JsonValueKind.Object}&&payload.Value.TryGetProperty("name",out var value))name=value.GetString();
        return await ExecuteWrite(async()=>{var copy=await designs.DuplicateAsync(TenantId,userId,templateKey,name,Ip(),Agent(),ct);return Ok(new{ok=true,message="Modelo duplicado como rascunho.",templateKey=copy.TemplateKey,redirectUrl=Url.Action(nameof(Edit),new{templateKey=copy.TemplateKey})});},"duplicar",templateKey);
    }

    [HttpGet("/Labels/Designer/DeleteDraft/{templateKey}")]
    public Task<IActionResult> DeleteDraft(string templateKey,CancellationToken ct)=>Details(templateKey,ct);

    [HttpPost("/Labels/Designer/DeleteDraft/{templateKey}"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerDelete)]
    public async Task<IActionResult> DeleteDraftConfirmed(string templateKey,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();return await ExecuteWrite(async()=>{await designs.DeleteDraftAsync(TenantId,userId,templateKey,Ip(),Agent(),ct);return Ok(new{ok=true,message="Rascunho excluído.",redirectUrl=Url.Action(nameof(Index))});},"excluir",templateKey);
    }

    [HttpGet("/Labels/Designer/Versions/{templateKey}")]
    [HttpGet("/Labels/Designer/{templateKey}/Versions")]
    public async Task<IActionResult> Versions(string templateKey,CancellationToken ct)
    {var design=await designs.GetAsync(TenantId,templateKey,ct);return design is null?NotFound():View("~/Views/Labels/Designer/Versions.cshtml",new LabelCanvasVersionsPageViewModel(design,await designs.GetVersionsAsync(TenantId,templateKey,ct)));}

    [HttpGet("/Labels/Designer/Versions/{templateKey}/Preview/{versionId:guid}")]
    [Authorize(Policy=AppPolicies.LabelDesignerPreview)]
    public async Task<IActionResult> PreviewVersion(string templateKey,Guid versionId,CancellationToken ct)
    {
        var design=await designs.GetVersionDesignAsync(TenantId,templateKey,versionId,ct);if(design is null)return NotFound();var rendered=renderer.Render(design,fieldCatalog.GetSampleData(SampleProfile(design)));
        await designs.RecordEventAsync(TenantId,UserId,design.Id,"PREVIEW_TEMPLATE","Preview de versão publicada gerado.",new{versionId,rendered.SnapshotHash},Ip(),Agent(),ct);
        return View("~/Views/Labels/Designer/Preview.cshtml",new LabelCanvasDesignerPageViewModel(design,fieldCatalog.GetFields(design.SubjectType),rendered.Validation,rendered.Html));
    }

    [HttpPost("/Labels/Designer/Versions/{templateKey}/Duplicate/{versionId:guid}"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> DuplicateVersion(string templateKey,Guid versionId,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();return await ExecuteWrite(async()=>{var draft=await designs.DuplicateVersionAsync(TenantId,userId,templateKey,versionId,Ip(),Agent(),ct);return Ok(new{ok=true,message="Versão duplicada como novo rascunho.",redirectUrl=Url.Action(nameof(Edit),new{templateKey=draft.TemplateKey})});},"duplicar versão",templateKey);
    }

    [HttpPost("/Labels/Designer/Versions/{templateKey}/Restore/{versionId:guid}"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> Restore(string templateKey,Guid versionId,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();return await ExecuteWrite(async()=>{var draft=await designs.RestoreVersionAsync(TenantId,userId,templateKey,versionId,Ip(),Agent(),ct);return Ok(new{ok=true,message="Versão restaurada como novo rascunho.",redirectUrl=Url.Action(nameof(Edit),new{templateKey=draft.TemplateKey})});},"restaurar",templateKey);
    }

    [HttpGet("/Labels/Designer/Fields")]
    public IActionResult Fields(string subjectType)=>Ok(fieldCatalog.GetFields(subjectType));

    private LabelCanvasDesignerPageViewModel Page(LabelCanvasDesignDto design,bool isNew=false){var fields=fieldCatalog.GetFields(design.SubjectType);return new(design,fields,renderer.Validate(design.DesignJson,fields.Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)),null,isNew);}
    private async Task<IActionResult> ExecuteWrite(Func<Task<IActionResult>> action,string operation,string templateKey)
    {try{return await action();}catch(KeyNotFoundException e){logger.LogWarning(e,"Template {TemplateKey} não encontrado ao {Operation}.",templateKey,operation);return NotFound(new{ok=false,message=e.Message});}catch(ArgumentException e){logger.LogWarning(e,"Entrada inválida ao {Operation} {TemplateKey}.",operation,templateKey);return BadRequest(new{ok=false,message=e.Message});}catch(InvalidOperationException e){logger.LogWarning(e,"Operação recusada ao {Operation} {TemplateKey}.",operation,templateKey);return BadRequest(new{ok=false,message=e.Message});}catch(Exception e){logger.LogError(e,"Erro ao {Operation} o template {TemplateKey}.",operation,templateKey);return StatusCode(500,new{ok=false,message="Não foi possível concluir a operação. Tente novamente."});}}
    private static LabelCanvasSaveRequest CopyWithKey(LabelCanvasSaveRequest x,string key)=>new(){TemplateKey=key,TemplateName=x.TemplateName,Description=x.Description,TemplateKind=x.TemplateKind,SubjectType=x.SubjectType,PaperKind=x.PaperKind,WidthMm=x.WidthMm,HeightMm=x.HeightMm,Orientation=x.Orientation,DesignJson=x.DesignJson,ChangeSummary=x.ChangeSummary};
    private string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();private string? Agent()=>Request.Headers.UserAgent.ToString();
    private static string SampleProfile(LabelCanvasDesignDto design)=>design.SubjectType.Equals("LocDeskFolder",StringComparison.OrdinalIgnoreCase)?"HOL":design.SubjectType.Contains("Box",StringComparison.OrdinalIgnoreCase)?"Caixa GED":"Documento GED";
    private static LabelCanvasDocumentDto NewDocument(string subject,string profile,decimal width,decimal height)=>new(){Canvas=new(){WidthMm=width,HeightMm=height,Paper="A4",Orientation="portrait",GridMm=2,SafeMarginMm=3},Bindings=new(){SubjectType=subject,SampleDataProfile=profile},Elements=[new(){Id="title",Type="text",Name="Título",XMm=5,YMm=5,WidthMm=70,HeightMm=9,Text="NOVA ETIQUETA",ZIndex=1,Style=new(){FontSizePt=10,FontWeight="700",Align="center"},Validation=new(){Required=true}}]};
}
