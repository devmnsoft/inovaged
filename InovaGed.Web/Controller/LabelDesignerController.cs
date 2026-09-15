using System.Text.Json;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Labels.Intelligence;
using InovaGed.Application.Branding;
using InovaGed.Application.Security;
using Dapper;
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
    ILabelCanvasStarterTemplateService starters, ILabelCanvasDiffService diffService, ILabelCanvasSchemaCapabilities schemaCapabilities,
    ILabelCanvasComponentPresetService componentPresets, ILabelTemplatePackageService packages,
    ILabelCanvasValueResolver valueResolver, IGedAccessPolicyService accessPolicy,
    IPrintBrandingProfileService brandingProfiles, IPrintBrandingResolver brandingResolver, ILabelPreflightService preflight,
    ILogger<LabelDesignerController> logger) : GedControllerBase(dbFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [HttpGet("/Labels/Designer")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var capability = await schemaCapabilities.GetAsync(ct);
        if (!capability.HasLabelTemplateDesign)
            return View("~/Views/Labels/Designer/Index.cshtml", new LabelDesignerIndexViewModel([], false, "Uma atualização de estrutura do banco é necessária para habilitar edição colaborativa de modelos.", false));
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
                    WidthMm = d.WidthMm,
                    HeightMm = d.HeightMm,
                    Health = d.Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase) ? "Em edição" : "Pronto",
                    IsSystem = d.IsSystemTemplate,
                    IsLegacy = isLegacy,
                    UpdatedAt = d.UpdatedAt ?? d.CreatedAt,
                    CanEdit = d.CanEdit,
                    BrandingProfileId = profile?.ProfileId
                };
            }).ToList();
            
            return View("~/Views/Labels/Designer/Index.cshtml", new LabelDesignerIndexViewModel(vm, capability.HasLockVersion, capability.HasLockVersion ? null : "Uma atualização de estrutura do banco é necessária para habilitar edição colaborativa de modelos.", capability.HasLockVersion));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Não foi possível listar templates canvas.");
            throw;
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

    [HttpGet("/Labels/Designer/{templateKey}/Export")]
    public async Task<IActionResult> Export(string templateKey,CancellationToken ct)
    {
        var design=await designs.GetAsync(TenantId,templateKey,ct);if(design is null)return NotFound();
        var package=packages.Export(design);var bytes=JsonSerializer.SerializeToUtf8Bytes(package,JsonOptions);
        var safe=Regex.Replace(design.TemplateName.Normalize(NormalizationForm.FormD),"[^a-zA-Z0-9_-]+","-").Trim('-').ToLowerInvariant();
        return File(bytes,"application/json",$"modelo-etiqueta-{(string.IsNullOrWhiteSpace(safe)?"modelo":safe)}.json");
    }

    [HttpPost("/Labels/Designer/Import"),ValidateAntiForgeryToken,RequestSizeLimit(1_000_000)]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> Import(IFormFile? file,bool confirm=false,CancellationToken ct=default)
    {
        if(UserId is not Guid userId)return Unauthorized();
        if(file is null||file.Length==0||file.Length>1_000_000)return BadRequest(new{message="Selecione um pacote JSON de até 1 MB."});
        try
        {
            using var reader=new StreamReader(file.OpenReadStream(),Encoding.UTF8);var json=await reader.ReadToEndAsync(ct);var package=packages.Validate(json,fieldCatalog);
            if(!confirm)return Ok(new{valid=true,name=package.Template.Name,purpose=FriendlyPurpose(package.Template.SubjectType),size=$"{package.Template.WidthMm} × {package.Template.HeightMm} mm",elementCount=package.Design.Elements.Count,warnings=Array.Empty<string>()});
            var key=await UniqueKeyAsync(package.Template.Name,ct);var request=new LabelCanvasSaveRequest{TemplateKey=key,TemplateName=package.Template.Name.Trim(),Description=package.Template.Description,SubjectType=package.Template.SubjectType,PaperKind=package.Template.PaperKind,WidthMm=package.Template.WidthMm,HeightMm=package.Template.HeightMm,Orientation=package.Template.Orientation,DesignJson=JsonSerializer.Serialize(package.Design,JsonOptions),LabelContext="GENERIC",ChangeSummary="Modelo importado de pacote versionado."};
            var created=await designs.CreateDraftAsync(TenantId,userId,request,Ip(),Agent(),ct);return Ok(new{ok=true,templateKey=created.TemplateKey,redirectUrl=Url.Action(nameof(Edit),new{templateKey=created.TemplateKey})});
        }
        catch(LabelCanvasRequestException exception){return BadRequest(new{code=exception.Code,message=exception.Message});}
    }

    [HttpGet("/Labels/Designer/ComponentPresets")]
    public async Task<IActionResult> ComponentPresets(CancellationToken ct)
    {
        var capability=await schemaCapabilities.GetAsync(ct);if(!capability.HasComponentPreset)return Ok(new{available=false,message="Atualização do banco necessária para blocos personalizados.",items=Array.Empty<object>()});
        return Ok(new{available=true,items=await componentPresets.ListAsync(TenantId,ct)});
    }

    [HttpGet("/Labels/Designer/ComponentPresets/{id:guid}")]
    public async Task<IActionResult> ComponentPreset(Guid id,CancellationToken ct)=>await componentPresets.GetAsync(TenantId,id,ct) is { } preset?Ok(preset):NotFound();

    [HttpPost("/Labels/Designer/ComponentPresets"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerUpdate)]
    public async Task<IActionResult> CreateComponentPreset([FromBody]LabelCanvasComponentPresetCreateRequest request,CancellationToken ct)
    {if(UserId is not Guid userId)return Unauthorized();try{return Ok(await componentPresets.CreateAsync(TenantId,userId,request,ct));}catch(LabelCanvasRequestException ex){return BadRequest(new{code=ex.Code,message=ex.Message});}catch(LabelSchemaUpdateRequiredException ex){return Conflict(new{code=LabelSchemaUpdateRequiredException.Code,message=ex.Message});}}

    public sealed record RenamePresetRequest(string Name);
    [HttpPost("/Labels/Designer/ComponentPresets/{id:guid}/Rename"),ValidateAntiForgeryToken,Authorize(Policy=AppPolicies.LabelDesignerUpdate)]
    public async Task<IActionResult> RenameComponentPreset(Guid id,[FromBody]RenamePresetRequest request,CancellationToken ct){if(UserId is not Guid userId)return Unauthorized();return Ok(await componentPresets.RenameAsync(TenantId,userId,id,request.Name,ct));}
    [HttpPost("/Labels/Designer/ComponentPresets/{id:guid}/Duplicate"),ValidateAntiForgeryToken,Authorize(Policy=AppPolicies.LabelDesignerUpdate)]
    public async Task<IActionResult> DuplicateComponentPreset(Guid id,[FromBody]RenamePresetRequest? request,CancellationToken ct){if(UserId is not Guid userId)return Unauthorized();return Ok(await componentPresets.DuplicateAsync(TenantId,userId,id,request?.Name,ct));}
    [HttpPost("/Labels/Designer/ComponentPresets/{id:guid}/Archive"),ValidateAntiForgeryToken,Authorize(Policy=AppPolicies.LabelDesignerUpdate)]
    public async Task<IActionResult> ArchiveComponentPreset(Guid id,CancellationToken ct){if(UserId is not Guid userId)return Unauthorized();await componentPresets.ArchiveAsync(TenantId,userId,id,ct);return Ok(new{ok=true});}

    [HttpGet("/Labels/Designer/New")]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> New(CancellationToken ct)
    {
        if (!(await schemaCapabilities.GetAsync(ct)).HasLockVersion)
            return Conflict(new { success=false, code=LabelSchemaUpdateRequiredException.Code, message=LabelSchemaUpdateRequiredException.FriendlyMessage });
        return View("~/Views/Labels/Designer/New.cshtml",new LabelCanvasNewModelInput{BrandingProfiles=await brandingProfiles.ListAsync(TenantId,ct)});
    }

    [HttpPost("/Labels/Designer/New"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> New(LabelCanvasNewModelInput input,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();
        if (!(await schemaCapabilities.GetAsync(ct)).HasLockVersion)
            return Conflict(new { success=false, code=LabelSchemaUpdateRequiredException.Code, message=LabelSchemaUpdateRequiredException.FriendlyMessage });
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
        return await ExecuteWrite(async()=>{var saved=await designs.SaveDraftAsync(TenantId,userId,request,Ip(),Agent(),ct);return Ok(new{ok=true,message="Rascunho salvo.",updatedAt=saved.UpdatedAt,lockVersion=saved.LockVersion});},"salvar",templateKey);
    }

    [HttpGet("/Labels/Designer/{templateKey}")]
    public async Task<IActionResult> Details(string templateKey,CancellationToken ct)
    { var design=await designs.GetAsync(TenantId,templateKey,ct); return design is null?NotFound():View("~/Views/Labels/Designer/Details.cshtml",await PageAsync(design,false,ct)); }

    [HttpPost("/Labels/Designer/Validate/{templateKey}")]
    [HttpPost("/Labels/Designer/{templateKey}/Validate")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerPreview)]
    public async Task<IActionResult> ValidateDesign(string templateKey,[FromBody] LabelCanvasSaveRequest? request,CancellationToken ct)
    {
        var design=await designs.GetAsync(TenantId,templateKey,ct);if(design is null)return NotFound();
        var json=request?.DesignJson??design.DesignJson;var allowed=fieldCatalog.GetFields(request?.SubjectType??design.SubjectType).Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);var validation=renderer.Validate(json,allowed);
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

    [HttpGet("/Labels/Designer/{templateKey}/PreviewSubjects")]
    [Authorize(Policy=AppPolicies.LabelDesignerPreview)]
    public async Task<IActionResult> PreviewSubjects(string templateKey, string? q, int limit = 20, CancellationToken ct = default)
    {
        if (UserId is not Guid userId) return Unauthorized();
        var design = await designs.GetAsync(TenantId, templateKey, ct);
        if (design is null) return NotFound();
        if (!await accessPolicy.CanAccessGedAsync(TenantId, userId, User, ct)) return Forbid();
        limit = Math.Clamp(limit, 1, 20);
        var term = string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";
        var isAdmin = await accessPolicy.IsAdminAsync(TenantId, userId, User, ct);
        var isBox = LabelCanvasSubjectTypeMapper.ToOperational(design.SubjectType) == "BOX";
        await using var db = await DbFactory.OpenAsync(ct);
        var sql = isBox ? """
select b.id Id,
       coalesce(to_jsonb(b)->>'label_code',to_jsonb(b)->>'box_code',to_jsonb(b)->>'code',to_jsonb(b)->>'box_no',b.id::text) Reference,
       coalesce(to_jsonb(b)->>'title',to_jsonb(b)->>'description',to_jsonb(b)->>'name','Caixa') PrimaryText,
       coalesce(to_jsonb(b)->>'location',to_jsonb(b)->>'notes') SecondaryText
  from ged.box b
 where b.tenant_id=@tenantId and coalesce(to_jsonb(b)->>'reg_status','A') in ('A','ACTIVE')
   and (@isAdmin or to_jsonb(b)->>'created_by'=@userIdText)
   and (@term is null or concat_ws(' ',to_jsonb(b)->>'label_code',to_jsonb(b)->>'box_code',to_jsonb(b)->>'code',to_jsonb(b)->>'box_no',to_jsonb(b)->>'title',to_jsonb(b)->>'description') ilike @term)
 order by coalesce(to_jsonb(b)->>'updated_at',to_jsonb(b)->>'created_at') desc nulls last limit @limit
""" : """
select d.id Id,
       coalesce(to_jsonb(d)->>'code',to_jsonb(d)->>'document_code',to_jsonb(d)->>'protocol_number',d.id::text) Reference,
       coalesce(to_jsonb(d)->>'title',to_jsonb(d)->>'name','Documento') PrimaryText,
       coalesce(to_jsonb(v)->>'file_name',to_jsonb(d)->>'description') SecondaryText
  from ged.document d
  left join ged.document_version v on v.id=d.current_version_id and v.tenant_id=d.tenant_id
 where d.tenant_id=@tenantId and coalesce(to_jsonb(d)->>'reg_status','A') in ('A','ACTIVE')
   and (@isAdmin or to_jsonb(d)->>'created_by'=@userIdText)
   and (@term is null or concat_ws(' ',to_jsonb(d)->>'title',to_jsonb(d)->>'name',to_jsonb(v)->>'file_name',to_jsonb(d)->>'code',to_jsonb(d)->>'document_code',to_jsonb(d)->>'protocol_number') ilike @term)
 order by coalesce(to_jsonb(d)->>'updated_at',to_jsonb(d)->>'created_at') desc nulls last limit @limit
""";
        var items = (await db.QueryAsync<LabelCanvasPreviewSubjectDto>(new CommandDefinition(sql,
            new { tenantId=TenantId, userIdText=userId.ToString(), isAdmin, term, limit }, cancellationToken:ct))).AsList();
        return Ok(new { items });
    }

    [HttpPost("/Labels/Designer/{templateKey}/PreviewSubject"), ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerPreview)]
    public async Task<IActionResult> PreviewSubject(string templateKey, [FromBody] LabelCanvasPreviewSubjectRequest request, CancellationToken ct)
    {
        if (UserId is not Guid userId) return Unauthorized();
        var design = await designs.GetAsync(TenantId, templateKey, ct);
        if (design is null) return NotFound();
        if (!await accessPolicy.CanAccessGedAsync(TenantId, userId, User, ct)) return Forbid();
        if (request.SubjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.DesignJson) || request.DesignJson.Length > 1_000_000) return BadRequest(new { message="Dados da prévia inválidos." });
        var operational = LabelCanvasSubjectTypeMapper.ToOperational(design.SubjectType);
        // The same conservative ACL used by the search is checked again to prevent ID probing.
        var allowed = await PreviewSubjectAccessibleAsync(operational, request.SubjectId, userId, ct);
        if (!allowed) return Forbid();
        var values = await valueResolver.ResolveAsync(TenantId, design.SubjectType, operational, request.SubjectId, ct);
        if (values.Count == 0) return NotFound(new { message="Registro não encontrado ou sem acesso." });
        var transient = CopyDesign(design, request.DesignJson);
        var preview = await ResolveDesignerPreviewValuesAsync(transient, values, request.BrandingProfileId, ct);
        var rendered = renderer.Render(transient, preview.Values);
        return Ok(new { ok=!rendered.Validation.HasErrors, html=rendered.Html, validation=rendered.Validation, branding=new { preview.Branding.ProfileName, preview.Branding.ClientName } });
    }

    [HttpPost("/Labels/Designer/{templateKey}/TestLab"), ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerPreview)]
    public async Task<IActionResult> TestLab(string templateKey, [FromBody] LabelCanvasTestLabRequest request, CancellationToken ct)
    {
        if (UserId is not Guid userId) return Unauthorized();
        var design = await designs.GetAsync(TenantId, templateKey, ct);
        if (design is null) return NotFound();
        if (!await accessPolicy.CanAccessGedAsync(TenantId, userId, User, ct)) return Forbid();
        var subjectIds = request.SubjectIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (subjectIds.Length is < 1 or > 5 || string.IsNullOrWhiteSpace(request.DesignJson) || request.DesignJson.Length > 1_000_000)
            return BadRequest(new { message="Selecione de 1 a 5 registros para testar." });
        var operational = LabelCanvasSubjectTypeMapper.ToOperational(design.SubjectType);
        var transient = CopyDesign(design, request.DesignJson);
        var results = new List<object>(subjectIds.Length);
        var ready = 0;
        foreach (var subjectId in subjectIds)
        {
            if (!await PreviewSubjectAccessibleAsync(operational, subjectId, userId, ct)) return Forbid();
            var values = await valueResolver.ResolveAsync(TenantId, design.SubjectType, operational, subjectId, ct);
            if (values.Count == 0) return NotFound(new { message="Registro não encontrado ou sem acesso." });
            var preview = await ResolveDesignerPreviewValuesAsync(transient, values, request.BrandingProfileId, ct);
            var rendered = renderer.Render(transient, preview.Values);
            var checkedResult = await preflight.CheckAsync(new(TenantId, userId, design.TemplateKey, operational, subjectId,
                request.BrandingProfileId, DesignJson:request.DesignJson, AllowDraftPreview:true), ct);
            var issues = rendered.Validation.Issues.Select(issue => new { issue.Code, issue.Severity, issue.Message, issue.ElementId })
                .Concat(checkedResult.Items.Select(issue => new { issue.Code, issue.Severity, issue.Message, issue.ElementId })).ToArray();
            var canPublish = !issues.Any(x => x.Severity == LabelPreflightSeverity.Error);
            if (canPublish) ready++;
            results.Add(new { subjectId, html=rendered.Html, canPublish, issues });
        }
        return Ok(new { tested=results.Count, ready, results });
    }

    [HttpPost("/Labels/Designer/StarterPreview"), ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerPreview)]
    public async Task<IActionResult> StarterPreview([FromBody] LabelCanvasStarterPreviewRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<LabelCanvasStarterKind>(request.StarterKind.Replace("_", ""), true, out var kind) ||
            request.WidthMm is < LabelCanvasDimensionPolicy.MinWidthMm or > LabelCanvasDimensionPolicy.MaxWidthMm ||
            request.HeightMm is < LabelCanvasDimensionPolicy.MinHeightMm or > LabelCanvasDimensionPolicy.MaxHeightMm)
            return BadRequest(new { message="Layout ou dimensões inválidos." });
        var document = starters.Create(new(request.SubjectType, kind, request.WidthMm, request.HeightMm, request.BrandingProfileId));
        var design = new LabelCanvasDesignDto { Id=Guid.Empty, TenantId=TenantId, TemplateKey="STARTER_PREVIEW", TemplateName="Prévia", SubjectType=request.SubjectType, WidthMm=request.WidthMm, HeightMm=request.HeightMm, DesignJson=JsonSerializer.Serialize(document,JsonOptions), DefaultBrandingProfileId=request.BrandingProfileId, CreatedAt=DateTime.UtcNow };
        var preview = await ResolveDesignerPreviewValuesAsync(design, SampleProfile(design), request.BrandingProfileId, ct);
        var rendered = renderer.Render(design, preview.Values);
        return Ok(new { ok=!rendered.Validation.HasErrors, html=rendered.Html, validation=rendered.Validation });
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
                ExpectedLockVersion = draft.LockVersion,
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

    [HttpPost("/Labels/Designer/{templateKey}/DuplicateConflict"),ValidateAntiForgeryToken]
    [Authorize(Policy=AppPolicies.LabelDesignerCreate)]
    public async Task<IActionResult> DuplicateConflict(string templateKey,[FromBody] LabelCanvasSaveRequest local,CancellationToken ct)
    {
        if(UserId is not Guid userId)return Unauthorized();
        return await ExecuteWrite(async()=>
        {
            var copy=await designs.DuplicateAsync(TenantId,userId,templateKey,$"{local.TemplateName} — cópia recuperada",Ip(),Agent(),ct);
            local=CopyWithKey(local,copy.TemplateKey,copy.LockVersion);
            var saved=await designs.SaveDraftAsync(TenantId,userId,local,Ip(),Agent(),ct);
            return Ok(new{ok=true,message="Cópia criada com suas alterações.",templateKey=saved.TemplateKey,redirectUrl=Url.Action(nameof(Edit),new{templateKey=saved.TemplateKey})});
        },"recuperar conflito",templateKey);
    }

    [HttpPost("/Labels/Designer/{templateKey}/CompareConflict")]
    [HttpPost("/Labels/Designer/{templateKey}/CompareLocal")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareConflict(string templateKey,[FromBody] LabelCanvasSaveRequest local,CancellationToken ct)
    {
        var server=await designs.GetAsync(TenantId,templateKey,ct);if(server is null)return NotFound();
        return Ok(diffService.Compare(server.DesignJson,local.DesignJson));
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
        return await ResolveDesignerPreviewValuesAsync(design,values,brandingProfileId,ct);
    }
    private async Task<(Dictionary<string,object?> Values,ResolvedPrintBranding Branding)> ResolveDesignerPreviewValuesAsync(LabelCanvasDesignDto design,IReadOnlyDictionary<string,object?> source,Guid? brandingProfileId,CancellationToken ct)
    {
        var values=source.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
        var branding=await brandingResolver.ResolveAsync(TenantId,PrintBrandingContext.LabelTemplate,design.BrandingBindingKey??design.TemplateKey,brandingProfileId??design.DefaultBrandingProfileId,null,ct);
        values["clientName"]=branding.ClientName??design.ClientNameFallback??values.GetValueOrDefault("clientName");values["contractName"]=branding.ContractName??design.ContractNameFallback??values.GetValueOrDefault("contractName");values["organizationName"]=branding.OrganizationName??design.OrganizationNameFallback??values.GetValueOrDefault("organizationName");
        values["headerTitle"]=branding.HeaderTitle??design.HeaderTitleFallback??values.GetValueOrDefault("headerTitle")??"ARQUIVO CENTRAL";values["headerSubtitle"]=branding.HeaderSubtitle??design.HeaderSubtitleFallback;values["headerExtraLine"]=branding.HeaderExtraLine;values["footerText"]=branding.FooterText;values["footerExtraLine"]=branding.FooterExtraLine;
        values["primaryLogo"]=branding.PrimaryLogoAssetId is Guid primary?$"/Administration/BrandAssets/{primary}/File":null;values["secondaryLogo"]=branding.SecondaryLogoAssetId is Guid secondary?$"/Administration/BrandAssets/{secondary}/File":null;
        return(values,branding);
    }
    private async Task<bool> PreviewSubjectAccessibleAsync(string operational,Guid subjectId,Guid userId,CancellationToken ct)
    {
        var isAdmin=await accessPolicy.IsAdminAsync(TenantId,userId,User,ct);
        var table=operational=="BOX"?"box":"document";
        await using var db=await DbFactory.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition($"select exists(select 1 from ged.{table} e where e.tenant_id=@tenantId and e.id=@subjectId and coalesce(to_jsonb(e)->>'reg_status','A') in ('A','ACTIVE') and (@isAdmin or to_jsonb(e)->>'created_by'=@userIdText))",new{tenantId=TenantId,subjectId,isAdmin,userIdText=userId.ToString()},cancellationToken:ct));
    }
    private static LabelCanvasDesignDto CopyDesign(LabelCanvasDesignDto d,string designJson)=>new(){Id=d.Id,TenantId=d.TenantId,TemplateKey=d.TemplateKey,TemplateName=d.TemplateName,Description=d.Description,TemplateKind=d.TemplateKind,SubjectType=d.SubjectType,PaperKind=d.PaperKind,WidthMm=d.WidthMm,HeightMm=d.HeightMm,Orientation=d.Orientation,Status=d.Status,DesignJson=designJson,CurrentVersion=d.CurrentVersion,IsSystemTemplate=d.IsSystemTemplate,DefaultBrandingProfileId=d.DefaultBrandingProfileId,BrandingBindingKey=d.BrandingBindingKey,ClientNameFallback=d.ClientNameFallback,ContractNameFallback=d.ContractNameFallback,OrganizationNameFallback=d.OrganizationNameFallback,HeaderTitleFallback=d.HeaderTitleFallback,HeaderSubtitleFallback=d.HeaderSubtitleFallback,LabelContext=d.LabelContext,CreatedAt=d.CreatedAt,LockVersion=d.LockVersion};
    private async Task<IActionResult> ExecuteWrite(Func<Task<IActionResult>> action,string operation,string templateKey)
    {try{return await action();}catch(LabelSchemaUpdateRequiredException e){logger.LogWarning("LABEL_SCHEMA_UPDATE_REQUIRED ao {Operation} {TemplateKey}.",operation,templateKey);return Conflict(new{ok=false,code=LabelSchemaUpdateRequiredException.Code,message=e.Message});}catch(LabelCanvasConflictException e){logger.LogWarning("Conflito otimista ao {Operation} {TemplateKey}.",operation,templateKey);return Conflict(new{ok=false,code="DESIGN_CONFLICT",message=e.Message});}catch(KeyNotFoundException e){logger.LogWarning(e,"Template {TemplateKey} não encontrado ao {Operation}.",templateKey,operation);return NotFound(new{ok=false,message=e.Message});}catch(LabelCanvasRequestException e){logger.LogWarning("LABEL_CANVAS_SAVE_REJECTED TemplateKey={TemplateKey} Operation={Operation} Code={Code}: {Message}",templateKey,operation,e.Code,e.Message);return BadRequest(new{ok=false,code=e.Code,message=e.Message,errors=e.Errors});}catch(ArgumentException e){logger.LogWarning(e,"Entrada inválida ao {Operation} {TemplateKey}.",operation,templateKey);return BadRequest(new{ok=false,code="INVALID_REQUEST",message=e.Message,errors=new Dictionary<string,string>()});}catch(InvalidOperationException e){logger.LogWarning(e,"Operação recusada ao {Operation} {TemplateKey}.",operation,templateKey);return BadRequest(new{ok=false,message=e.Message});}catch(Exception e){logger.LogError(e,"Erro ao {Operation} o template {TemplateKey}.",operation,templateKey);return StatusCode(500,new{ok=false,message="Não foi possível concluir a operação. Tente novamente."});}}
    private static LabelCanvasSaveRequest CopyWithKey(LabelCanvasSaveRequest x,string key,long? expectedLockVersion=null)=>new(){ExpectedLockVersion=expectedLockVersion??x.ExpectedLockVersion,TemplateKey=key,TemplateName=x.TemplateName,Description=x.Description,TemplateKind=x.TemplateKind,SubjectType=x.SubjectType,PaperKind=x.PaperKind,WidthMm=x.WidthMm,HeightMm=x.HeightMm,Orientation=x.Orientation,DesignJson=x.DesignJson,DefaultBrandingProfileId=x.DefaultBrandingProfileId,BrandingBindingKey=x.BrandingBindingKey,ClientNameFallback=x.ClientNameFallback,ContractNameFallback=x.ContractNameFallback,OrganizationNameFallback=x.OrganizationNameFallback,HeaderTitleFallback=x.HeaderTitleFallback,HeaderSubtitleFallback=x.HeaderSubtitleFallback,LabelContext=x.LabelContext,ChangeSummary=x.ChangeSummary};
    private string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();private string? Agent()=>Request.Headers.UserAgent.ToString();
    private static string SampleProfile(LabelCanvasDesignDto design)=>design.SubjectType.Equals("LocDeskFolder",StringComparison.OrdinalIgnoreCase)?"HOL":design.SubjectType.Contains("Box",StringComparison.OrdinalIgnoreCase)?"Caixa GED":"Documento GED";
    private static LabelCanvasDocumentDto NewDocument(string subject,string profile,decimal width,decimal height)=>new(){Canvas=new(){WidthMm=width,HeightMm=height,Paper="A4",Orientation="portrait",GridMm=2,SafeMarginMm=3},Bindings=new(){SubjectType=subject,SampleDataProfile=profile},Elements=[new(){Id="title",Type="field",Name="Título do cabeçalho",XMm=5,YMm=5,WidthMm=70,HeightMm=9,ZIndex=1,Binding=new(){Field="headerTitle",Fallback="ARQUIVO CENTRAL"},Style=new(){FontSizePt=10,FontWeight="700",Align="center"},Validation=new(){Required=true}}]};
}
