using System.Text.Json;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Branding;
using InovaGed.Web.Models.Labels;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.LabelDesignerRead)]
public sealed class LabelDesignerController(IDbConnectionFactory dbFactory, ILabelCanvasDesignService designs,
    ILabelCanvasRenderService renderer, ILabelCanvasFieldCatalogService fieldCatalog,
    ILabelCanvasStarterTemplateService starters, ILabelCanvasDiffService diffService,
    IPrintBrandingProfileService brandingProfiles, IPrintBrandingResolver brandingResolver,
    ILogger<LabelDesignerController> logger) : GedControllerBase(dbFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [HttpGet("/Labels/Designer")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            var list = await designs.ListAsync(TenantId, ct);
            var profiles = await brandingProfiles.ListAsync(TenantId, ct);
            var validProfiles = profiles.Where(p => p.ProfileId is not null && p.ProfileId != Guid.Empty).ToList();
            var duplicateProfileIds = validProfiles.GroupBy(p => p.ProfileId!.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicateProfileIds.Count > 0)
                logger.LogWarning("Foram encontrados {Count} identificadores de perfil de branding duplicados ao listar modelos de etiqueta.", duplicateProfileIds.Count);
            var profilesDict = validProfiles.GroupBy(p => p.ProfileId!.Value).ToDictionary(g => g.Key, g => g.First());
            
            var vm = list.Select(d =>
            {
                var profile = d.DefaultBrandingProfileId.HasValue && profilesDict.TryGetValue(d.DefaultBrandingProfileId.Value, out var p) ? p : null;
                var isLegacy = d.LabelContext.Equals("LEGACY", StringComparison.OrdinalIgnoreCase);
                var clientName = FirstNonEmpty(profile?.ClientName, d.ClientNameFallback);
                clientName ??= isLegacy ? "Compatibilidade" : d.IsSystemTemplate ? "Modelo padrão" : "Sem identidade definida";

                var brandingName = FirstNonEmpty(profile?.ProfileName);
                brandingName ??= profile is not null || d.IsSystemTemplate ? "Padrão automático" : "Sem perfil definido";
                
                return new LabelTemplateListItemViewModel
                {
                    TemplateKey = d.TemplateKey,
                    Name = d.TemplateName,
                    Purpose = FriendlyPurpose(d.SubjectType),
                    ClientName = clientName,
                    BrandingProfileName = brandingName,
                    Status = d.Status,
                    Version = d.CurrentVersion.ToString(),
                    Dimensions = $"{Math.Round(d.WidthMm)} × {Math.Round(d.HeightMm)} mm",
                    Health = d.Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase) ? "Em edição" : "Pronto",
                    IsSystem = d.IsSystemTemplate,
                    IsLegacy = isLegacy,
                    UpdatedAt = d.UpdatedAt ?? d.CreatedAt,
                    CanEdit = d.CanEdit,
                    BrandingProfileId = profile?.ProfileId
                };
            }).ToList();
            
            return View("~/Views/Labels/Designer/Index.cshtml", vm);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Não foi possível listar templates canvas.");
            ViewBag.SchemaPending = true;
            return View("~/Views/Labels/Designer/Index.cshtml", new List<LabelTemplateListItemViewModel>());
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string FriendlyPurpose(string? purpose) => purpose?.Trim().ToUpperInvariant() switch
    {
        "BOX" => "Caixa",
        "DOCUMENT" => "Documento",
        "FOLDER" => "Pasta",
        "MEDICALRECORD" => "Prontuário",
        "PROCESS" => "Processo",
        "MANUALLABEL" => "Etiqueta avulsa",
        "LOCDESKFOLDER" => "Pasta antiga",
        "LOCDESKBOX" => "Caixa antiga",
        "PHYSICALLOCATION" => "Localização física",
        "CLASSIFICATION" => "Classificação",
        "LOAN" => "Empréstimo",
        "PROTOCOL" => "Protocolo",
        _ => "Uso geral"
    };

    [HttpGet("/Labels/Designer/New")]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> New(CancellationToken ct)
    {
        return View("~/Views/Labels/Designer/New.cshtml",new LabelCanvasNewModelInput{BrandingProfiles=await brandingProfiles.ListAsync(TenantId,ct)});
    }

    [HttpPost("/Labels/Designer/New"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> New(LabelCanvasNewModelInput input,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();
        if(!Enum.TryParse<LabelCanvasStarterKind>(input.StarterKind.Replace("_", ""),true,out var kind))ModelState.AddModelError(nameof(input.StarterKind),"Escolha um layout inicial válido.");
        var allowedSubjects=new[]{"Box","Document","Folder","MedicalRecord","Process","ManualLabel"};
        if(!allowedSubjects.Contains(input.SubjectType,StringComparer.OrdinalIgnoreCase))ModelState.AddModelError(nameof(input.SubjectType),"Escolha uma finalidade válida.");
        if(!LabelPaperOptions.IsSupported(input.PaperKind))ModelState.AddModelError(nameof(input.PaperKind),"Escolha um papel suportado.");
        if(!ModelState.IsValid){input.BrandingProfiles=await brandingProfiles.ListAsync(TenantId,ct);return View("~/Views/Labels/Designer/New.cshtml",input);}
        var key=await UniqueKeyAsync(input.Name,ct);
        var document=starters.Create(new(input.SubjectType,kind,input.WidthMm,input.HeightMm,input.BrandingProfileId,input.PaperKind));
        var request=new LabelCanvasSaveRequest{TemplateKey=key,TemplateName=input.Name.Trim(),TemplateKind="CANVAS",SubjectType=input.SubjectType,PaperKind=input.PaperKind,WidthMm=input.WidthMm,HeightMm=input.HeightMm,DesignJson=JsonSerializer.Serialize(document,JsonOptions),DefaultBrandingProfileId=input.BrandingProfileId,LabelContext="GENERIC",ChangeSummary="Modelo criado pelo assistente inicial."};
        var created=await designs.CreateDraftAsync(TenantId,userId,request,Ip(),Agent(),ct);
        return RedirectToAction(nameof(Edit),new{templateKey=created.TemplateKey});
    }

    private async Task<string> UniqueKeyAsync(string name,CancellationToken ct)
    {
        var normalized=name.Normalize(NormalizationForm.FormD);var chars=normalized.Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray();
        var baseKey=Regex.Replace(new string(chars).ToUpperInvariant(),"[^A-Z0-9]+","_").Trim('_');if(string.IsNullOrWhiteSpace(baseKey))baseKey="MODELO_ETIQUETA";baseKey=baseKey[..Math.Min(baseKey.Length,70)];
        var key=baseKey;for(var suffix=2;await designs.GetAsync(TenantId,key,ct) is not null;suffix++)key=$"{baseKey}_{suffix}";return key;
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
        logger.LogInformation("LABEL_CANVAS_SAVE_REQUEST TemplateKey={TemplateKey} WidthMm={WidthMm} HeightMm={HeightMm} PaperKind={PaperKind} SubjectType={SubjectType}",templateKey,request.WidthMm,request.HeightMm,request.PaperKind,request.SubjectType);
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

    [HttpPost("/Labels/Designer/Revision/{templateKey}"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerUpdate)]
    public async Task<IActionResult> BeginRevision(string templateKey,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();
        return await ExecuteWrite(async()=>{var draft=await designs.BeginRevisionAsync(TenantId,userId,templateKey,Ip(),Agent(),ct);return Ok(new{ok=true,message=$"Revisão baseada na versão {draft.CurrentVersion} criada.",redirectUrl=Url.Action(nameof(Edit),new{templateKey})});},"criar revisão",templateKey);
    }

    [HttpGet("/Labels/Designer/{templateKey}/Thumbnail")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Thumbnail(string templateKey, CancellationToken ct)
    {
        var design = await designs.GetAsync(TenantId, templateKey, ct);
        if (design is null) return NotFound();
        var preview = await ResolveDesignerPreviewValuesAsync(design, SampleProfile(design), design.DefaultBrandingProfileId, ct);
        var rendered = renderer.Render(design, preview.Values, false);
        return Content(rendered.Html, "text/html; charset=utf-8");
    }

    [HttpPost("/Labels/Designer/DuplicateForClient/{templateKey}")]
    [HttpPost("/Labels/Designer/{templateKey}/DuplicateForClient")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> DuplicateForClient(string templateKey, [FromBody] JsonElement? payload, CancellationToken ct)
    {
        if (UserId is not Guid userId) return Unauthorized();
        string? name = null;
        if (payload is {ValueKind:JsonValueKind.Object} && payload.Value.TryGetProperty("name", out var value)) name = value.GetString();
        
        return await ExecuteWrite(async () =>
        {
            var draft = await designs.DuplicateAsync(TenantId, userId, templateKey, name, Ip(), Agent(), ct);
            
            // Clear previous brand data
            var request = new LabelCanvasSaveRequest
            {
                TemplateKey = draft.TemplateKey,
                TemplateName = draft.TemplateName,
                TemplateKind = draft.TemplateKind,
                SubjectType = draft.SubjectType,
                PaperKind = draft.PaperKind,
                WidthMm = draft.WidthMm,
                HeightMm = draft.HeightMm,
                Orientation = draft.Orientation,
                DesignJson = draft.DesignJson,
                DefaultBrandingProfileId = null, // clear branding
                BrandingBindingKey = null,
                ClientNameFallback = null,
                ContractNameFallback = null,
                OrganizationNameFallback = null,
                HeaderTitleFallback = null,
                HeaderSubtitleFallback = null,
                LabelContext = draft.LabelContext,
                ChangeSummary = "Duplicado para outro cliente"
            };
            await designs.SaveDraftAsync(TenantId, userId, request, Ip(), Agent(), ct);

            return Ok(new{ok=true,message="Cópia criada.",redirectUrl=Url.Action(nameof(Edit),new{templateKey=draft.TemplateKey})});
        }, "duplicar para cliente", templateKey);
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

    [HttpGet("/Labels/Designer/{templateKey}/Compare/{versionNo:int}")]
    public async Task<IActionResult> Compare(string templateKey, int versionNo, CancellationToken ct)
    {
        var current = await designs.GetAsync(TenantId, templateKey, ct);
        var published = await designs.GetPublishedAsync(TenantId, templateKey, versionNo, ct);
        if (current is null || published is null) return NotFound();
        return Ok(diffService.Compare(published.DesignJson, current.DesignJson));
    }

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
        if(UserId is not Guid userId)return Unauthorized();return await ExecuteWrite(async()=>{var draft=await designs.RestoreVersionAsync(TenantId,userId,templateKey,versionId,Ip(),Agent(),ct);return Ok(new{ok=true,message="Versão restaurada na revisão da mesma chave.",redirectUrl=Url.Action(nameof(Edit),new{templateKey=draft.TemplateKey})});},"restaurar",templateKey);
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
    {try{return await action();}catch(KeyNotFoundException e){logger.LogWarning(e,"Template {TemplateKey} não encontrado ao {Operation}.",templateKey,operation);return NotFound(new{ok=false,message=e.Message});}catch(LabelCanvasRequestException e){logger.LogWarning("LABEL_CANVAS_SAVE_REJECTED TemplateKey={TemplateKey} Operation={Operation} Code={Code}: {Message}",templateKey,operation,e.Code,e.Message);return BadRequest(new{ok=false,code=e.Code,message=e.Message,errors=e.Errors});}catch(ArgumentException e){logger.LogWarning(e,"Entrada inválida ao {Operation} {TemplateKey}.",operation,templateKey);return BadRequest(new{ok=false,code="INVALID_REQUEST",message=e.Message,errors=new Dictionary<string,string>()});}catch(InvalidOperationException e){logger.LogWarning(e,"Operação recusada ao {Operation} {TemplateKey}.",operation,templateKey);return BadRequest(new{ok=false,message=e.Message});}catch(Exception e){logger.LogError(e,"Erro ao {Operation} o template {TemplateKey}.",operation,templateKey);return StatusCode(500,new{ok=false,message="Não foi possível concluir a operação. Tente novamente."});}}
    private static LabelCanvasSaveRequest CopyWithKey(LabelCanvasSaveRequest x,string key)=>new(){TemplateKey=key,TemplateName=x.TemplateName,Description=x.Description,TemplateKind=x.TemplateKind,SubjectType=x.SubjectType,PaperKind=x.PaperKind,WidthMm=x.WidthMm,HeightMm=x.HeightMm,Orientation=x.Orientation,DesignJson=x.DesignJson,DefaultBrandingProfileId=x.DefaultBrandingProfileId,BrandingBindingKey=x.BrandingBindingKey,ClientNameFallback=x.ClientNameFallback,ContractNameFallback=x.ContractNameFallback,OrganizationNameFallback=x.OrganizationNameFallback,HeaderTitleFallback=x.HeaderTitleFallback,HeaderSubtitleFallback=x.HeaderSubtitleFallback,LabelContext=x.LabelContext,ChangeSummary=x.ChangeSummary};
    private string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();private string? Agent()=>Request.Headers.UserAgent.ToString();
    private static string SampleProfile(LabelCanvasDesignDto design)=>design.SubjectType.Equals("LocDeskFolder",StringComparison.OrdinalIgnoreCase)?"HOL":design.SubjectType.Contains("Box",StringComparison.OrdinalIgnoreCase)?"Caixa GED":"Documento GED";
    private static LabelCanvasDocumentDto NewDocument(string subject,string profile,decimal width,decimal height)=>new(){Canvas=new(){WidthMm=width,HeightMm=height,Paper="A4",Orientation="portrait",GridMm=2,SafeMarginMm=3},Bindings=new(){SubjectType=subject,SampleDataProfile=profile},Elements=[new(){Id="title",Type="field",Name="Título do cabeçalho",XMm=5,YMm=5,WidthMm=70,HeightMm=9,ZIndex=1,Binding=new(){Field="headerTitle",Fallback="ARQUIVO CENTRAL"},Style=new(){FontSizePt=10,FontWeight="700",Align="center"},Validation=new(){Required=true}}]};
}
