using System.Data;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Labels;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
public sealed class LabelDesignerController(IDbConnectionFactory dbFactory) : GedControllerBase(dbFactory)
{
    private const string OfficialMessage = "Este é um modelo oficial do sistema. Para personalizar, duplique o modelo e edite a cópia.";
    private static readonly string[] OfficialCodes = ["FACTORY_BOX_V1", "FACTORY_DOCUMENT_V1", "LOCDESK_CAIXA_V1", "LOCDESK_PASTA_V1", "LOCDESK_PASTA_HOL_V1"];

    [HttpGet("/Labels/Designer")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var models = await ListAsync(ct);
        return View("~/Views/Labels/Designer/Index.cshtml", models);
    }

    [HttpGet("/Labels/Designer/{templateCode}")]
    public async Task<IActionResult> Details(string templateCode, CancellationToken ct) => await Show(templateCode, "Details", ct);

    [HttpGet("/Labels/Designer/{templateCode}/Edit")]
    public async Task<IActionResult> Edit(string templateCode, CancellationToken ct)
    {
        var model = await FindAsync(templateCode, ct);
        if (model is null) return NotFound();
        if (!model.CanEdit) TempData["Info"] = model.IsSystemTemplate ? OfficialMessage : "Somente modelos em rascunho podem ser editados.";
        return View("~/Views/Labels/Designer/Edit.cshtml", model);
    }

    [HttpPost("/Labels/Designer/{templateCode}/Save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string templateCode, SaveLabelDesignInput input, CancellationToken ct)
    {
        var model = await FindAsync(templateCode, ct);
        if (model is null) return NotFound();
        if (!model.CanEdit) return BadRequest(model.IsSystemTemplate ? OfficialMessage : "Somente rascunhos podem ser alterados.");
        List<LabelDesignFieldViewModel>? fields;
        try { fields = JsonSerializer.Deserialize<List<LabelDesignFieldViewModel>>(input.FieldsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { ModelState.AddModelError(nameof(input.FieldsJson), "Configuração de campos inválida."); fields = null; }
        if (string.IsNullOrWhiteSpace(input.TemplateName) || input.WidthMm <= 0 || input.HeightMm <= 0 || fields is null)
            return BadRequest("Nome, dimensões e configuração de campos são obrigatórios.");
        await using var db = await DbFactory.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("update ged.label_template_design set template_name=@name,description=@description,width_mm=@width,height_mm=@height,updated_at=now() where id=@id and tenant_id=@tenant and not is_system_template and status='DRAFT' and reg_status='A'", new { name=input.TemplateName.Trim(), input.Description, width=input.WidthMm, height=input.HeightMm, id=model.Id, tenant=TenantId }, tx, cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("update ged.label_template_design_field set reg_status='I' where template_design_id=@id and tenant_id=@tenant and reg_status='A'", new { id=model.Id, tenant=TenantId }, tx, cancellationToken:ct));
        var order=0; foreach (var f in fields.Where(x=>!string.IsNullOrWhiteSpace(x.FieldKey)).GroupBy(x=>x.FieldKey,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()))
            await db.ExecuteAsync(new CommandDefinition("insert into ged.label_template_design_field(tenant_id,template_design_id,field_key,field_label,field_type,data_source,x_mm,y_mm,width_mm,height_mm,font_size_pt,font_weight,text_align,color,is_required,is_printable,display_order) values(@tenant,@id,@FieldKey,@FieldLabel,@FieldType,@DataSource,@XMm,@YMm,@WidthMm,@HeightMm,@FontSizePt,@FontWeight,@TextAlign,@Color,@IsRequired,@IsPrintable,@order)", new { tenant=TenantId,id=model.Id,f.FieldKey,f.FieldLabel,f.FieldType,f.DataSource,f.XMm,f.YMm,f.WidthMm,f.HeightMm,f.FontSizePt,f.FontWeight,f.TextAlign,f.Color,f.IsRequired,f.IsPrintable,order=order++ },tx,cancellationToken:ct));
        await AuditAsync(db,tx,"LABEL_DESIGN_UPDATED",model.Id,templateCode,ct); await tx.CommitAsync(ct);
        TempData["Success"]="Modelo atualizado."; return RedirectToAction(nameof(Edit),new{templateCode});
    }

    [HttpPost("/Labels/Designer/{templateCode}/Duplicate"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(string templateCode, CancellationToken ct)
    {
        var source=await FindAsync(templateCode,ct); if(source is null)return NotFound();
        await using var db=await DbFactory.OpenAsync(ct); await using var tx=await db.BeginTransactionAsync(ct);
        var prefix="CUSTOM_"+templateCode.Replace("_V1","");
        var next=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)+1 from ged.label_template_design where tenant_id=@tenant and template_code like @prefix||'_%'",new{tenant=TenantId,prefix},tx,cancellationToken:ct));
        var code=$"{prefix}_{next:000}";
        var id=await db.ExecuteScalarAsync<Guid>(new CommandDefinition("insert into ged.label_template_design(tenant_id,template_code,template_name,description,subject_type,print_mode,view_name,status,width_mm,height_mm,paper_size,orientation,is_system_template,base_template_code,current_version,design_json,created_by) select @tenant,@code,template_name||' - Cópia',description,subject_type,'CUSTOM',view_name,'DRAFT',width_mm,height_mm,paper_size,orientation,false,template_code,1,design_json,@user from ged.label_template_design where id=@id returning id",new{tenant=TenantId,code,user=UserId,id=source.Id},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("insert into ged.label_template_design_field(tenant_id,template_design_id,field_key,field_label,field_type,data_source,x_mm,y_mm,width_mm,height_mm,font_size_pt,font_weight,text_align,color,is_required,is_printable,display_order) select @tenant,@newId,field_key,field_label,field_type,data_source,x_mm,y_mm,width_mm,height_mm,font_size_pt,font_weight,text_align,color,is_required,is_printable,display_order from ged.label_template_design_field where template_design_id=@sourceId and reg_status='A'",new{tenant=TenantId,newId=id,sourceId=source.Id},tx,cancellationToken:ct));
        await AuditAsync(db,tx,"LABEL_DESIGN_DUPLICATED",id,code,ct); await tx.CommitAsync(ct);
        return RedirectToAction(nameof(Edit),new{templateCode=code});
    }

    [HttpPost("/Labels/Designer/{templateCode}/Validate"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Validate(string templateCode,CancellationToken ct)
    {
        var model=await FindAsync(templateCode,ct);if(model is null)return NotFound(); var issues=ValidateModel(model);
        await using var db=await DbFactory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("update ged.label_template_design_validation set reg_status='I' where template_design_id=@id and tenant_id=@tenant and reg_status='A'",new{id=model.Id,tenant=TenantId},tx,cancellationToken:ct));
        foreach(var v in issues) await db.ExecuteAsync(new CommandDefinition("insert into ged.label_template_design_validation(tenant_id,template_design_id,validation_type,severity,title,description,recommended_action,status) values(@tenant,@id,@type,@severity,@title,@description,'Revise o campo no editor visual.','OPEN')",new{tenant=TenantId,id=model.Id,type=v.ValidationType,severity=v.Severity,title=v.Title,description=v.Description},tx,cancellationToken:ct));
        if(issues.Count==0) await db.ExecuteAsync(new CommandDefinition("insert into ged.label_template_design_validation(tenant_id,template_design_id,validation_type,severity,title,status) values(@tenant,@id,'PASSED','INFO','Validação visual aprovada','RESOLVED')",new{tenant=TenantId,id=model.Id},tx,cancellationToken:ct));
        await AuditAsync(db,tx,"LABEL_DESIGN_VALIDATED",model.Id,templateCode,ct);await tx.CommitAsync(ct);TempData[issues.Count==0?"Success":"Info"]=issues.Count==0?"Modelo validado e pronto para publicação.":$"Validação encontrou {issues.Count} alerta(s).";return RedirectToAction(nameof(Details),new{templateCode});
    }

    [HttpPost("/Labels/Designer/{templateCode}/Publish"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string templateCode,CancellationToken ct)
    {
        var model=await FindAsync(templateCode,ct);if(model is null)return NotFound();if(model.IsSystemTemplate||model.Status!="DRAFT")return BadRequest("Apenas cópias CUSTOM em rascunho podem ser publicadas.");
        if(model.Validations.Count==0||model.Validations.Any(x=>x.Status=="OPEN"))return BadRequest("Valide e corrija os alertas antes de publicar.");
        await using var db=await DbFactory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);var version=model.CurrentVersion+1;
        var snapshot=JsonSerializer.Serialize(model);await db.ExecuteAsync(new CommandDefinition("insert into ged.label_template_design_version(tenant_id,template_design_id,version_number,snapshot_json,published_by) values(@tenant,@id,@version,cast(@snapshot as jsonb),@user); update ged.label_template_design set status='PUBLISHED',current_version=@version,updated_at=now() where id=@id and tenant_id=@tenant",new{tenant=TenantId,id=model.Id,version,snapshot,user=UserId},tx,cancellationToken:ct));
        await AuditAsync(db,tx,"LABEL_DESIGN_VERSION_CREATED",model.Id,templateCode,ct);await AuditAsync(db,tx,"LABEL_DESIGN_PUBLISHED",model.Id,templateCode,ct);await tx.CommitAsync(ct);return RedirectToAction(nameof(Details),new{templateCode});
    }

    [HttpGet("/Labels/Designer/{templateCode}/Versions")]
    public async Task<IActionResult> Versions(string templateCode,CancellationToken ct){var model=await FindAsync(templateCode,ct);if(model is null)return NotFound();await using var db=await DbFactory.OpenAsync(ct);ViewBag.Template=model;var versions=(await db.QueryAsync<LabelDesignVersionViewModel>(new CommandDefinition("select version_number VersionNumber,status Status,published_at PublishedAt,notes Notes from ged.label_template_design_version where template_design_id=@id and (tenant_id=@tenant or tenant_id is null) and reg_status='A' order by version_number desc",new{id=model.Id,tenant=TenantId},cancellationToken:ct))).AsList();return View("~/Views/Labels/Designer/Versions.cshtml",versions);}
    [HttpGet("/Labels/Designer/{templateCode}/Preview")]
    public async Task<IActionResult> Preview(string templateCode,CancellationToken ct)=>await Show(templateCode,"Preview",ct);
    [HttpGet("/Labels/Designer/{templateCode}/PrintTest")]
    public async Task<IActionResult> PrintTest(string templateCode,CancellationToken ct){var result=await Show(templateCode,"PrintTest",ct);if(result is ViewResult){await using var db=await DbFactory.OpenAsync(ct);await AuditAsync(db,null,"LABEL_DESIGN_PRINT_TESTED",((LabelDesignViewModel)((ViewResult)result).Model!).Id,templateCode,ct);}return result;}

    private async Task<IActionResult> Show(string code,string view,CancellationToken ct){var model=await FindAsync(code,ct);if(model is null)return NotFound();await using var db=await DbFactory.OpenAsync(ct);await AuditAsync(db,null,"LABEL_DESIGN_VIEWED",model.Id,code,ct);return View($"~/Views/Labels/Designer/{view}.cshtml",model);}
    private async Task<List<LabelDesignViewModel>> ListAsync(CancellationToken ct){await using var db=await DbFactory.OpenAsync(ct);if(!await HasTableAsync(db,"ged","label_template_design"))return [];return (await db.QueryAsync<LabelDesignViewModel>(new CommandDefinition("select id Id,template_code TemplateCode,template_name TemplateName,description Description,subject_type SubjectType,print_mode PrintMode,status Status,width_mm WidthMm,height_mm HeightMm,is_system_template IsSystemTemplate,current_version CurrentVersion,created_at CreatedAt,updated_at UpdatedAt from ged.label_template_design where (tenant_id=@tenant or tenant_id is null) and reg_status='A' order by is_system_template desc,updated_at desc nulls last,template_name",new{tenant=TenantId},cancellationToken:ct))).AsList();}
    private async Task<LabelDesignViewModel?> FindAsync(string code,CancellationToken ct){await using var db=await DbFactory.OpenAsync(ct);if(!await HasTableAsync(db,"ged","label_template_design"))return null;var m=await db.QuerySingleOrDefaultAsync<LabelDesignViewModel>(new CommandDefinition("select id Id,template_code TemplateCode,template_name TemplateName,description Description,subject_type SubjectType,print_mode PrintMode,status Status,width_mm WidthMm,height_mm HeightMm,paper_size PaperSize,orientation Orientation,is_system_template IsSystemTemplate,base_template_code BaseTemplateCode,current_version CurrentVersion,created_at CreatedAt,updated_at UpdatedAt from ged.label_template_design where (tenant_id=@tenant or tenant_id is null) and upper(template_code)=upper(@code) and reg_status='A' order by tenant_id nulls last limit 1",new{tenant=TenantId,code},cancellationToken:ct));if(m is null)return null;m.Fields=(await db.QueryAsync<LabelDesignFieldViewModel>(new CommandDefinition("select field_key FieldKey,field_label FieldLabel,field_type FieldType,data_source DataSource,x_mm XMm,y_mm YMm,width_mm WidthMm,height_mm HeightMm,font_size_pt FontSizePt,font_weight FontWeight,text_align TextAlign,color Color,is_required IsRequired,is_printable IsPrintable,display_order DisplayOrder from ged.label_template_design_field where template_design_id=@id and reg_status='A' order by display_order",new{id=m.Id},cancellationToken:ct))).AsList();m.Validations=(await db.QueryAsync<LabelDesignValidationViewModel>(new CommandDefinition("select validation_type ValidationType,severity Severity,title Title,description Description,status Status from ged.label_template_design_validation where template_design_id=@id and reg_status='A' order by created_at desc",new{id=m.Id},cancellationToken:ct))).AsList();return m;}
    private static List<LabelDesignValidationViewModel> ValidateModel(LabelDesignViewModel m){var r=new List<LabelDesignValidationViewModel>();void Add(string t,string s,string title,string d)=>r.Add(new(t,s,title,d,"OPEN"));if(string.IsNullOrWhiteSpace(m.TemplateName))Add("NAME","HIGH","Modelo sem nome","Informe o nome.");if(m.WidthMm<=0||m.HeightMm<=0)Add("DIMENSION","HIGH","Modelo sem dimensão","Informe largura e altura.");foreach(var f in m.Fields){if(f.XMm<0||f.YMm<0||f.XMm+f.WidthMm>m.WidthMm||f.YMm+f.HeightMm>m.HeightMm)Add("OUTSIDE","HIGH","Campo fora da área",f.FieldLabel);if(f.FontSizePt<6)Add("FONT","MEDIUM","Fonte menor que o limite legível",f.FieldLabel);if(f.FieldType=="QRCODE"&&(f.WidthMm<18||f.HeightMm<18))Add("QR_SIZE","HIGH","QR Code pequeno demais",f.FieldLabel);}for(var i=0;i<m.Fields.Count;i++)for(var j=i+1;j<m.Fields.Count;j++){var a=m.Fields[i];var b=m.Fields[j];if(a.XMm<b.XMm+b.WidthMm&&a.XMm+a.WidthMm>b.XMm&&a.YMm<b.YMm+b.HeightMm&&a.YMm+a.HeightMm>b.YMm)Add("OVERLAP","MEDIUM","Campos sobrepostos",$"{a.FieldLabel} e {b.FieldLabel}");}var keys=m.Fields.Select(x=>x.FieldKey.ToUpperInvariant()).ToHashSet();foreach(var required in RequiredKeys(m.TemplateCode))if(!keys.Contains(required))Add("REQUIRED","HIGH","Campo obrigatório ausente",required);return r;}
    private static IEnumerable<string> RequiredKeys(string code)=>code.Contains("HOL",StringComparison.OrdinalIgnoreCase)?["CONTRATO","CONTROLE","LOCALIZACAO","ASSUNTO","CLASSIFICACAO","BORDA"] : code.StartsWith("LOCDESK",StringComparison.OrdinalIgnoreCase)?["ARQUIVO","CONTROLE","VOLUME","LOCALIZACAO"]:[];
    private async Task AuditAsync(IDbConnection db,IDbTransaction? tx,string action,Guid designId,string code,CancellationToken ct)=>await db.ExecuteAsync(new CommandDefinition("insert into ged.label_template_design_audit(tenant_id,template_design_id,event_type,template_code,user_id) values(@tenant,@designId,@action,@code,@user)",new{tenant=TenantId,designId,action,code,user=UserId},tx,cancellationToken:ct));
}
