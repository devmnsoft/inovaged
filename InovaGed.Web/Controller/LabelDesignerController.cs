using System.Text.Json;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Branding;
using InovaGed.Web.Models.Labels;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.LabelDesignerRead)]
public sealed class LabelDesignerController(IDbConnectionFactory dbFactory, ILabelCanvasDesignService designs,
    ILabelCanvasRenderService renderer, ILabelCanvasFieldCatalogService fieldCatalog,
    IPrintBrandingProfileService brandingProfiles, IPrintBrandingResolver brandingResolver,
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
    public async Task<IActionResult> New(string? baseTemplate,CancellationToken ct)
    {
        var source=string.IsNullOrWhiteSpace(baseTemplate)?null:await designs.GetAsync(TenantId,baseTemplate,ct);
        var document=NewDocument("Document","Genérico",100,70);
        var design=source is null
            ?new LabelCanvasDesignDto { Id=Guid.Empty,TenantId=TenantId,TemplateName="Novo modelo de etiqueta",TemplateKind="CANVAS",SubjectType="Document",PaperKind="A4",WidthMm=100,HeightMm=70,Orientation="portrait",Status="DRAFT",DesignJson=JsonSerializer.Serialize(document,JsonOptions),CurrentVersion=1,HeaderTitleFallback="ARQUIVO CENTRAL",LabelContext="GENERIC",CreatedAt=DateTime.UtcNow }
            :new LabelCanvasDesignDto { Id=Guid.Empty,TenantId=TenantId,TemplateName=$"Novo modelo baseado em {source.TemplateName}",TemplateKind=source.TemplateKind,SubjectType=source.SubjectType,PaperKind=source.PaperKind,WidthMm=source.WidthMm,HeightMm=source.HeightMm,Orientation=source.Orientation,Status="DRAFT",DesignJson=source.DesignJson,CurrentVersion=1,DefaultBrandingProfileId=source.DefaultBrandingProfileId,BrandingBindingKey=source.BrandingBindingKey,ClientNameFallback=source.ClientNameFallback,ContractNameFallback=source.ContractNameFallback,OrganizationNameFallback=source.OrganizationNameFallback,HeaderTitleFallback=source.HeaderTitleFallback,HeaderSubtitleFallback=source.HeaderSubtitleFallback,LabelContext=source.LabelContext,CreatedAt=DateTime.UtcNow};
        ViewBag.BaseTemplate=baseTemplate;
        return View("~/Views/Labels/Designer/Edit.cshtml",await PageAsync(design,true,ct));
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
    { var design=await designs.GetAsync(TenantId,templateKey,ct); return design is null?NotFound():View("~/Views/Labels/Designer/Edit.cshtml",await PageAsync(design,false,ct)); }

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
    { var design=await designs.GetAsync(TenantId,templateKey,ct); return design is null?NotFound():View("~/Views/Labels/Designer/Details.cshtml",await PageAsync(design,false,ct)); }

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
    public async Task<IActionResult> Preview(string templateKey,string? profile,Guid? brandingProfileId,CancellationToken ct)
    {
        var design=await designs.GetAsync(TenantId,templateKey,ct);if(design is null)return NotFound();var selected=profile??SampleProfile(design);var preview=await ResolveDesignerPreviewValuesAsync(design,selected,brandingProfileId,ct);var rendered=renderer.Render(design,preview.Values);
        await designs.RecordEventAsync(TenantId,UserId,design.Id,"PREVIEW_TEMPLATE","Preview gerado.",new{profile=selected,rendered.SnapshotHash},Ip(),Agent(),ct);
        return View("~/Views/Labels/Designer/Preview.cshtml",new LabelCanvasDesignerPageViewModel(design,fieldCatalog.GetFields(design.SubjectType),rendered.Validation,rendered.Html));
    }

    [HttpGet("/Labels/Designer/TestPrint/{templateKey}")]
    [HttpGet("/Labels/Designer/{templateKey}/PrintTest")]
    [Authorize(Policy=AppPolicies.LabelDesignerPrintTest)]
    public async Task<IActionResult> TestPrint(string templateKey,string? profile,Guid? brandingProfileId,CancellationToken ct)
    {
        var design=await designs.GetAsync(TenantId,templateKey,ct);if(design is null)return NotFound();var preview=await ResolveDesignerPreviewValuesAsync(design,profile??SampleProfile(design),brandingProfileId,ct);var rendered=renderer.Render(design,preview.Values,true);
        await designs.RecordEventAsync(TenantId,UserId,design.Id,"TEST_PRINT","Impressão de teste gerada.",new{rendered.SnapshotHash},Ip(),Agent(),ct);return Content(rendered.Html,"text/html; charset=utf-8");
    }

    [HttpPost("/Labels/Designer/LivePreview"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerPreview)]
    public async Task<IActionResult> LivePreview([FromBody] LabelCanvasSaveRequest request,CancellationToken ct)
    {
        var design=new LabelCanvasDesignDto{Id=Guid.Empty,TenantId=TenantId,TemplateKey=request.TemplateKey,TemplateName=request.TemplateName,TemplateKind=request.TemplateKind,SubjectType=request.SubjectType,PaperKind=request.PaperKind,WidthMm=request.WidthMm,HeightMm=request.HeightMm,Orientation=request.Orientation,Status="DRAFT",DesignJson=request.DesignJson,DefaultBrandingProfileId=request.DefaultBrandingProfileId,BrandingBindingKey=request.BrandingBindingKey,ClientNameFallback=request.ClientNameFallback,ContractNameFallback=request.ContractNameFallback,OrganizationNameFallback=request.OrganizationNameFallback,HeaderTitleFallback=request.HeaderTitleFallback,HeaderSubtitleFallback=request.HeaderSubtitleFallback,LabelContext=request.LabelContext,CreatedAt=DateTime.UtcNow};
        var preview=await ResolveDesignerPreviewValuesAsync(design,SampleProfile(design),request.DefaultBrandingProfileId,ct);var rendered=renderer.Render(design,preview.Values);
        return Ok(new{ok=!rendered.Validation.HasErrors,html=rendered.Html,validation=rendered.Validation,branding=new{preview.Branding.ProfileId,preview.Branding.ProfileName,preview.Branding.ClientName,preview.Branding.ContractName,preview.Branding.OrganizationName}});
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

    private async Task<LabelCanvasDesignerPageViewModel> PageAsync(LabelCanvasDesignDto design,bool isNew,CancellationToken ct){var fields=fieldCatalog.GetFields(design.SubjectType);return new(design,fields,renderer.Validate(design.DesignJson,fields.Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)),null,isNew,await brandingProfiles.ListAsync(TenantId,ct));}
    private async Task<(Dictionary<string,object?> Values,ResolvedPrintBranding Branding)> ResolveDesignerPreviewValuesAsync(LabelCanvasDesignDto design,string sampleProfile,Guid? brandingProfileId,CancellationToken ct)
    {
        var values=fieldCatalog.GetSampleData(sampleProfile).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
        var branding=await brandingResolver.ResolveAsync(TenantId,PrintBrandingContext.LabelTemplate,design.BrandingBindingKey??design.TemplateKey,brandingProfileId??design.DefaultBrandingProfileId,null,ct);
        values["clientName"]=branding.ClientName??design.ClientNameFallback??values.GetValueOrDefault("clientName");values["contractName"]=branding.ContractName??design.ContractNameFallback??values.GetValueOrDefault("contractName");values["organizationName"]=branding.OrganizationName??design.OrganizationNameFallback??values.GetValueOrDefault("organizationName");
        values["headerTitle"]=branding.HeaderTitle??design.HeaderTitleFallback??values.GetValueOrDefault("headerTitle")??"ARQUIVO CENTRAL";values["headerSubtitle"]=branding.HeaderSubtitle??design.HeaderSubtitleFallback;values["headerExtraLine"]=branding.HeaderExtraLine;values["footerText"]=branding.FooterText;values["footerExtraLine"]=branding.FooterExtraLine;
        values["primaryLogo"]=branding.PrimaryLogoAssetId is Guid primary?$"/Administration/BrandAssets/{primary}/File":null;values["secondaryLogo"]=branding.SecondaryLogoAssetId is Guid secondary?$"/Administration/BrandAssets/{secondary}/File":null;
        return(values,branding);
    }
    private async Task<IActionResult> ExecuteWrite(Func<Task<IActionResult>> action,string operation,string templateKey)
    {try{return await action();}catch(KeyNotFoundException e){logger.LogWarning(e,"Template {TemplateKey} não encontrado ao {Operation}.",templateKey,operation);return NotFound(new{ok=false,message=e.Message});}catch(ArgumentException e){logger.LogWarning(e,"Entrada inválida ao {Operation} {TemplateKey}.",operation,templateKey);return BadRequest(new{ok=false,message=e.Message});}catch(InvalidOperationException e){logger.LogWarning(e,"Operação recusada ao {Operation} {TemplateKey}.",operation,templateKey);return BadRequest(new{ok=false,message=e.Message});}catch(Exception e){logger.LogError(e,"Erro ao {Operation} o template {TemplateKey}.",operation,templateKey);return StatusCode(500,new{ok=false,message="Não foi possível concluir a operação. Tente novamente."});}}
    private static LabelCanvasSaveRequest CopyWithKey(LabelCanvasSaveRequest x,string key)=>new(){TemplateKey=key,TemplateName=x.TemplateName,Description=x.Description,TemplateKind=x.TemplateKind,SubjectType=x.SubjectType,PaperKind=x.PaperKind,WidthMm=x.WidthMm,HeightMm=x.HeightMm,Orientation=x.Orientation,DesignJson=x.DesignJson,DefaultBrandingProfileId=x.DefaultBrandingProfileId,BrandingBindingKey=x.BrandingBindingKey,ClientNameFallback=x.ClientNameFallback,ContractNameFallback=x.ContractNameFallback,OrganizationNameFallback=x.OrganizationNameFallback,HeaderTitleFallback=x.HeaderTitleFallback,HeaderSubtitleFallback=x.HeaderSubtitleFallback,LabelContext=x.LabelContext,ChangeSummary=x.ChangeSummary};
    private string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();private string? Agent()=>Request.Headers.UserAgent.ToString();
    private static string SampleProfile(LabelCanvasDesignDto design)=>design.SubjectType.Equals("LocDeskFolder",StringComparison.OrdinalIgnoreCase)?"HOL":design.SubjectType.Contains("Box",StringComparison.OrdinalIgnoreCase)?"Caixa GED":"Documento GED";
    private static LabelCanvasDocumentDto NewDocument(string subject,string profile,decimal width,decimal height)=>new(){Canvas=new(){WidthMm=width,HeightMm=height,Paper="A4",Orientation="portrait",GridMm=2,SafeMarginMm=3},Bindings=new(){SubjectType=subject,SampleDataProfile=profile},Elements=[new(){Id="title",Type="field",Name="Título do cabeçalho",XMm=5,YMm=5,WidthMm=70,HeightMm=9,ZIndex=1,Binding=new(){Field="headerTitle",Fallback="ARQUIVO CENTRAL"},Style=new(){FontSizePt=10,FontWeight="700",Align="center"},Validation=new(){Required=true}}]};
}
