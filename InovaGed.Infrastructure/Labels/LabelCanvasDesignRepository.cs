using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasDesignRepository(
    IDbConnectionFactory dbFactory,
    ILabelCanvasRenderService renderer,
    ILabelCanvasFieldCatalogService fields,
    ILogger<LabelCanvasDesignRepository> logger) : ILabelCanvasDesignService
{
    private const string SelectColumns = "id Id,tenant_id TenantId,template_key TemplateKey,template_name TemplateName,description Description,template_kind TemplateKind,subject_type SubjectType,paper_kind PaperKind,width_mm WidthMm,height_mm HeightMm,orientation Orientation,status Status,design_json::text DesignJson,current_version CurrentVersion,is_system_template IsSystemTemplate,default_branding_profile_id DefaultBrandingProfileId,branding_binding_key BrandingBindingKey,client_name_fallback ClientNameFallback,contract_name_fallback ContractNameFallback,organization_name_fallback OrganizationNameFallback,header_title_fallback HeaderTitleFallback,header_subtitle_fallback HeaderSubtitleFallback,label_context LabelContext,created_by CreatedBy,created_at CreatedAt,updated_by UpdatedBy,updated_at UpdatedAt,published_by PublishedBy,published_at PublishedAt";

    public async Task<IReadOnlyList<LabelCanvasDesignDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db=await dbFactory.OpenAsync(cancellationToken);
        const string sql="select "+SelectColumns+" from ged.label_template_design where (tenant_id=@tenantId or tenant_id is null) and reg_status in ('A','ACTIVE') order by status='DRAFT' desc,coalesce(updated_at,created_at) desc,template_name";
        return (await db.QueryAsync<LabelCanvasDesignDto>(new CommandDefinition(sql,new{tenantId},cancellationToken:cancellationToken))).AsList();
    }

    public async Task<LabelCanvasDesignDto?> GetAsync(Guid tenantId,string templateKey,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(templateKey))return null;
        await using var db=await dbFactory.OpenAsync(cancellationToken);
        const string sql="select "+SelectColumns+" from ged.label_template_design where (tenant_id=@tenantId or tenant_id is null) and upper(template_key)=upper(@templateKey) and reg_status in ('A','ACTIVE') order by tenant_id nulls last limit 1";
        return await db.QuerySingleOrDefaultAsync<LabelCanvasDesignDto>(new CommandDefinition(sql,new{tenantId,templateKey},cancellationToken:cancellationToken));
    }

    public async Task<LabelCanvasDesignDto> CreateDraftAsync(Guid tenantId,Guid userId,LabelCanvasSaveRequest request,string? ipAddress,string? userAgent,CancellationToken cancellationToken=default)
    {
        ValidateIdentity(tenantId,userId); ValidateRequest(request);
        var validation=ValidateDesign(request);
        if(validation.HasErrors)throw ValidationException(validation);
        await using var db=await dbFactory.OpenAsync(cancellationToken);await using var tx=await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var id=Guid.NewGuid();var parameters=Parameters(request,tenantId,userId,id);
            const string sql="""
insert into ged.label_template_design(id,tenant_id,template_key,template_code,template_name,description,template_kind,print_mode,subject_type,paper_kind,paper_size,width_mm,height_mm,orientation,status,design_json,current_version,is_system_template,default_branding_profile_id,branding_binding_key,client_name_fallback,contract_name_fallback,organization_name_fallback,header_title_fallback,header_subtitle_fallback,label_context,created_by,created_at,reg_status)
values(@id,@tenantId,@TemplateKey,@TemplateKey,@TemplateName,@Description,@TemplateKind,'CUSTOM',@SubjectType,@PaperKind,@PaperKind,@WidthMm,@HeightMm,@Orientation,'DRAFT',cast(@DesignJson as jsonb),1,false,@DefaultBrandingProfileId,@BrandingBindingKey,@ClientNameFallback,@ContractNameFallback,@OrganizationNameFallback,@HeaderTitleFallback,@HeaderSubtitleFallback,@LabelContext,@userId,now(),'ACTIVE');
""";
            await db.ExecuteAsync(new CommandDefinition(sql,parameters,tx,cancellationToken:cancellationToken));
            await InsertEventAsync(db,tx,tenantId,userId,id,"CREATE_DRAFT","Rascunho criado.",new{request.TemplateKey},ipAddress,userAgent,cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return (await GetAsync(tenantId,request.TemplateKey,cancellationToken))!;
        }
        catch(Exception exception){await tx.RollbackAsync(cancellationToken);logger.LogError(exception,"Falha ao criar o template canvas {TemplateKey}.",request.TemplateKey);throw;}
    }

    public async Task<LabelCanvasDesignDto> SaveDraftAsync(Guid tenantId,Guid userId,LabelCanvasSaveRequest request,string? ipAddress,string? userAgent,CancellationToken cancellationToken=default)
    {
        ValidateIdentity(tenantId,userId);ValidateRequest(request);var validation=ValidateDesign(request);if(validation.HasErrors)throw ValidationException(validation);
        var current=await GetAsync(tenantId,request.TemplateKey,cancellationToken);
        if(current is { TenantId:null } && current.CanEdit)
        {
            var created=await CreateDraftAsync(tenantId,userId,request,ipAddress,userAgent,cancellationToken);
            await RecordEventAsync(tenantId,userId,created.Id,"MATERIALIZE_SEED","Modelo inicial materializado como rascunho do tenant.",new{sourceId=current.Id},ipAddress,userAgent,cancellationToken);
            return created;
        }
        await using var db=await dbFactory.OpenAsync(cancellationToken);await using var tx=await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var parameters=Parameters(request,tenantId,userId,Guid.Empty);
            const string sql="""
update ged.label_template_design set template_name=@TemplateName,description=@Description,template_kind=@TemplateKind,subject_type=@SubjectType,paper_kind=@PaperKind,paper_size=@PaperKind,width_mm=@WidthMm,height_mm=@HeightMm,orientation=@Orientation,design_json=cast(@DesignJson as jsonb),default_branding_profile_id=@DefaultBrandingProfileId,branding_binding_key=@BrandingBindingKey,client_name_fallback=@ClientNameFallback,contract_name_fallback=@ContractNameFallback,organization_name_fallback=@OrganizationNameFallback,header_title_fallback=@HeaderTitleFallback,header_subtitle_fallback=@HeaderSubtitleFallback,label_context=@LabelContext,updated_by=@userId,updated_at=now()
where tenant_id=@tenantId and upper(template_key)=upper(@TemplateKey) and status='DRAFT' and not is_system_template and reg_status in ('A','ACTIVE') returning id;
""";
            var id=await db.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql,parameters,tx,cancellationToken:cancellationToken));
            if(id is null)throw new InvalidOperationException("Somente templates próprios em rascunho podem ser alterados.");
            await InsertEventAsync(db,tx,tenantId,userId,id.Value,"UPDATE_DRAFT",request.ChangeSummary??"Rascunho atualizado.",new{validation.Issues},ipAddress,userAgent,cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return (await GetAsync(tenantId,request.TemplateKey,cancellationToken))!;
        }
        catch(Exception exception){await tx.RollbackAsync(cancellationToken);logger.LogError(exception,"Falha ao salvar o template canvas {TemplateKey}.",request.TemplateKey);throw;}
    }

    public async Task<LabelCanvasDesignDto> PublishAsync(Guid tenantId,Guid userId,LabelCanvasPublishRequest request,string? ipAddress,string? userAgent,CancellationToken cancellationToken=default)
    {
        ValidateIdentity(tenantId,userId);if(string.IsNullOrWhiteSpace(request.TemplateKey))throw new ArgumentException("Template obrigatório.",nameof(request));
        var design=await GetAsync(tenantId,request.TemplateKey,cancellationToken)??throw new KeyNotFoundException("Template não encontrado.");
        if(!design.CanEdit)throw new InvalidOperationException("Somente templates próprios em rascunho podem ser publicados.");
        if(design.TenantId is null)
        {
            var seed=design;
            design=await CreateDraftAsync(tenantId,userId,SaveRequest(seed,"Modelo inicial preparado para publicação."),ipAddress,userAgent,cancellationToken);
            await RecordEventAsync(tenantId,userId,design.Id,"MATERIALIZE_SEED","Modelo inicial materializado como rascunho do tenant.",new{sourceId=seed.Id},ipAddress,userAgent,cancellationToken);
        }
        var allowed=fields.GetFields(design.SubjectType).Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);var validation=renderer.Validate(design.DesignJson,allowed);
        if(validation.HasErrors)throw ValidationException(validation);if(validation.HasWarnings&&!request.ConfirmWarnings)throw new InvalidOperationException("A publicação possui alertas. Revise-os ou confirme a publicação consciente.");
        await using var db=await dbFactory.OpenAsync(cancellationToken);await using var tx=await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var version=await db.ExecuteScalarAsync<int>(new CommandDefinition("select coalesce(max(version_no),0)+1 from ged.label_template_design_version where template_design_id=@id",new{id=design.Id},tx,cancellationToken:cancellationToken));
            var hash=renderer.ComputeSnapshotHash(design.DesignJson);
            const string insert="""
insert into ged.label_template_design_version(id,tenant_id,template_design_id,version_no,version_number,status,design_json,snapshot_json,change_summary,notes,created_by,created_at,published_by,published_at,snapshot_hash,reg_status)
values(gen_random_uuid(),@tenantId,@id,@version,@version,'PUBLISHED',cast(@json as jsonb),cast(@json as jsonb),@summary,@summary,@userId,now(),@userId,now(),@hash,'ACTIVE');
update ged.label_template_design set status='PUBLISHED',current_version=@version,published_by=@userId,published_at=now(),updated_by=@userId,updated_at=now() where id=@id and tenant_id=@tenantId;
""";
            await db.ExecuteAsync(new CommandDefinition(insert,new{tenantId,id=design.Id,version,json=design.DesignJson,summary=request.ChangeSummary,userId,hash},tx,cancellationToken:cancellationToken));
            await InsertEventAsync(db,tx,tenantId,userId,design.Id,"PUBLISH_TEMPLATE",request.ChangeSummary??$"Versão {version} publicada.",new{version,hash,validation.Issues},ipAddress,userAgent,cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return (await GetAsync(tenantId,request.TemplateKey,cancellationToken))!;
        }
        catch(Exception exception){await tx.RollbackAsync(cancellationToken);logger.LogError(exception,"Falha ao publicar o template canvas {TemplateKey}.",request.TemplateKey);throw;}
    }

    public async Task<LabelCanvasDesignDto> DuplicateAsync(Guid tenantId,Guid userId,string templateKey,string? newName,string? ipAddress,string? userAgent,CancellationToken cancellationToken=default)
    {
        var source=await GetAsync(tenantId,templateKey,cancellationToken)??throw new KeyNotFoundException("Template não encontrado.");
        var suffix=DateTime.UtcNow.ToString("yyyyMMddHHmmss");var baseKey=Regex.Replace(templateKey.ToUpperInvariant(),"[^A-Z0-9_]+","_").Trim('_');var key=$"{baseKey}_COPY_{suffix}";
        var document=JsonSerializer.Deserialize<LabelCanvasDocumentDto>(source.DesignJson,new JsonSerializerOptions(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true})??new();
        var request=new LabelCanvasSaveRequest{TemplateKey=key,TemplateName=string.IsNullOrWhiteSpace(newName)?source.TemplateName+" - Cópia":newName.Trim(),Description=source.Description,TemplateKind=source.TemplateKind,SubjectType=source.SubjectType,PaperKind=source.PaperKind,WidthMm=source.WidthMm,HeightMm=source.HeightMm,Orientation=source.Orientation,DesignJson=JsonSerializer.Serialize(document,new JsonSerializerOptions(JsonSerializerDefaults.Web)),DefaultBrandingProfileId=source.DefaultBrandingProfileId,BrandingBindingKey=source.BrandingBindingKey,ClientNameFallback=source.ClientNameFallback,ContractNameFallback=source.ContractNameFallback,OrganizationNameFallback=source.OrganizationNameFallback,HeaderTitleFallback=source.HeaderTitleFallback,HeaderSubtitleFallback=source.HeaderSubtitleFallback,LabelContext=source.LabelContext};
        var created=await CreateDraftAsync(tenantId,userId,request,ipAddress,userAgent,cancellationToken);
        await RecordEventAsync(tenantId,userId,created.Id,"DUPLICATE_TEMPLATE",$"Duplicado de {templateKey}.",new{sourceId=source.Id,sourceKey=templateKey},ipAddress,userAgent,cancellationToken);
        return created;
    }

    public async Task DeleteDraftAsync(Guid tenantId,Guid userId,string templateKey,string? ipAddress,string? userAgent,CancellationToken cancellationToken=default)
    {
        ValidateIdentity(tenantId,userId);await using var db=await dbFactory.OpenAsync(cancellationToken);await using var tx=await db.BeginTransactionAsync(cancellationToken);
        var id=await db.ExecuteScalarAsync<Guid?>(new CommandDefinition("update ged.label_template_design set reg_status='INACTIVE',archived_at=now(),updated_by=@userId,updated_at=now() where tenant_id=@tenantId and upper(template_key)=upper(@templateKey) and status='DRAFT' and not is_system_template and reg_status in ('A','ACTIVE') returning id",new{tenantId,userId,templateKey},tx,cancellationToken:cancellationToken));
        if(id is null)throw new InvalidOperationException("Somente rascunhos próprios podem ser excluídos.");
        await InsertEventAsync(db,tx,tenantId,userId,id.Value,"DELETE_DRAFT","Rascunho excluído.",null,ipAddress,userAgent,cancellationToken);await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LabelCanvasVersionDto>> GetVersionsAsync(Guid tenantId,string templateKey,CancellationToken cancellationToken=default)
    {
        var design=await GetAsync(tenantId,templateKey,cancellationToken)??throw new KeyNotFoundException("Template não encontrado.");await using var db=await dbFactory.OpenAsync(cancellationToken);
        const string sql="""
select v.id Id,v.version_no VersionNo,v.status Status,v.change_summary ChangeSummary,v.created_by CreatedBy,v.created_at CreatedAt,
 v.published_by PublishedBy,v.published_at PublishedAt,v.snapshot_hash SnapshotHash,
 coalesce(cu.name,'Sistema') CreatedByName,coalesce(pu.name,case when v.published_by is null then null else 'Usuário registrado' end) PublishedByName
from ged.label_template_design_version v
left join ged.app_user cu on cu.tenant_id=@tenantId and cu.id=v.created_by
left join ged.app_user pu on pu.tenant_id=@tenantId and pu.id=v.published_by
where v.template_design_id=@id and v.reg_status in ('A','ACTIVE') order by v.version_no desc
""";
        return (await db.QueryAsync<LabelCanvasVersionDto>(new CommandDefinition(sql,new{id=design.Id},cancellationToken:cancellationToken))).AsList();
    }

    public async Task<LabelCanvasDesignDto?> GetVersionDesignAsync(Guid tenantId,string templateKey,Guid versionId,CancellationToken cancellationToken=default)
    {
        var design=await GetAsync(tenantId,templateKey,cancellationToken);if(design is null)return null;await using var db=await dbFactory.OpenAsync(cancellationToken);
        var version=await db.QuerySingleOrDefaultAsync<VersionSnapshot>(new CommandDefinition("select design_json::text DesignJson,version_no VersionNo from ged.label_template_design_version where id=@versionId and template_design_id=@id and reg_status in ('A','ACTIVE')",new{versionId,id=design.Id},cancellationToken:cancellationToken));
        return version is null||string.IsNullOrWhiteSpace(version.DesignJson)?null:VersionDesign(design,version.DesignJson,version.VersionNo);
    }

    public async Task<LabelCanvasDesignDto> DuplicateVersionAsync(Guid tenantId,Guid userId,string templateKey,Guid versionId,string? ipAddress,string? userAgent,CancellationToken cancellationToken=default)
        =>await CreateFromVersionAsync(tenantId,userId,templateKey,versionId,"Cópia da versão","DUPLICATE_VERSION",ipAddress,userAgent,cancellationToken);

    public async Task<LabelCanvasDesignDto> RestoreVersionAsync(Guid tenantId,Guid userId,string templateKey,Guid versionId,string? ipAddress,string? userAgent,CancellationToken cancellationToken=default)
        =>await CreateFromVersionAsync(tenantId,userId,templateKey,versionId,"Restauração","RESTORE_VERSION",ipAddress,userAgent,cancellationToken);

    private async Task<LabelCanvasDesignDto> CreateFromVersionAsync(Guid tenantId,Guid userId,string templateKey,Guid versionId,string suffix,string eventType,string? ipAddress,string? userAgent,CancellationToken cancellationToken)
    {
        var source=await GetAsync(tenantId,templateKey,cancellationToken)??throw new KeyNotFoundException("Template não encontrado.");await using var db=await dbFactory.OpenAsync(cancellationToken);
        var json=await db.ExecuteScalarAsync<string?>(new CommandDefinition("select design_json::text from ged.label_template_design_version where id=@versionId and template_design_id=@id and reg_status in ('A','ACTIVE')",new{versionId,id=source.Id},cancellationToken:cancellationToken))??throw new KeyNotFoundException("Versão não encontrada.");
        var draft=await DuplicateAsync(tenantId,userId,templateKey,$"{source.TemplateName} - {suffix}",ipAddress,userAgent,cancellationToken);
        var request=new LabelCanvasSaveRequest{TemplateKey=draft.TemplateKey,TemplateName=draft.TemplateName,Description=$"Rascunho derivado de uma versão de {templateKey}.",TemplateKind=source.TemplateKind,SubjectType=source.SubjectType,PaperKind=source.PaperKind,WidthMm=source.WidthMm,HeightMm=source.HeightMm,Orientation=source.Orientation,DesignJson=json,ChangeSummary=$"{suffix} criada como novo rascunho."};
        var saved=await SaveDraftAsync(tenantId,userId,request,ipAddress,userAgent,cancellationToken);
        await RecordEventAsync(tenantId,userId,saved.Id,eventType,$"{suffix} criada como novo rascunho.",new{sourceId=source.Id,versionId},ipAddress,userAgent,cancellationToken);
        return saved;
    }

    public async Task RecordEventAsync(Guid tenantId,Guid? userId,Guid designId,string eventType,string? message,object? payload,string? ipAddress,string? userAgent,CancellationToken cancellationToken=default)
    {await using var db=await dbFactory.OpenAsync(cancellationToken);await InsertEventAsync(db,null,tenantId,userId,designId,eventType,message,payload,ipAddress,userAgent,cancellationToken);}

    private LabelCanvasValidationResult ValidateDesign(LabelCanvasSaveRequest request){var allowed=fields.GetFields(request.SubjectType).Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);return renderer.Validate(request.DesignJson,allowed);}
    private static LabelCanvasDesignDto VersionDesign(LabelCanvasDesignDto source,string json,int version)=>new(){Id=source.Id,TenantId=source.TenantId,TemplateKey=source.TemplateKey,TemplateName=source.TemplateName,Description=source.Description,TemplateKind=source.TemplateKind,SubjectType=source.SubjectType,PaperKind=source.PaperKind,WidthMm=source.WidthMm,HeightMm=source.HeightMm,Orientation=source.Orientation,Status="PUBLISHED",DesignJson=json,CurrentVersion=version,IsSystemTemplate=true,DefaultBrandingProfileId=source.DefaultBrandingProfileId,BrandingBindingKey=source.BrandingBindingKey,ClientNameFallback=source.ClientNameFallback,ContractNameFallback=source.ContractNameFallback,OrganizationNameFallback=source.OrganizationNameFallback,HeaderTitleFallback=source.HeaderTitleFallback,HeaderSubtitleFallback=source.HeaderSubtitleFallback,LabelContext=source.LabelContext,CreatedBy=source.CreatedBy,CreatedAt=source.CreatedAt,PublishedBy=source.PublishedBy,PublishedAt=source.PublishedAt};
    private static LabelCanvasSaveRequest SaveRequest(LabelCanvasDesignDto design,string? summary)=>new(){TemplateKey=design.TemplateKey,TemplateName=design.TemplateName,Description=design.Description,TemplateKind=design.TemplateKind,SubjectType=design.SubjectType,PaperKind=design.PaperKind,WidthMm=design.WidthMm,HeightMm=design.HeightMm,Orientation=design.Orientation,DesignJson=design.DesignJson,DefaultBrandingProfileId=design.DefaultBrandingProfileId,BrandingBindingKey=design.BrandingBindingKey,ClientNameFallback=design.ClientNameFallback,ContractNameFallback=design.ContractNameFallback,OrganizationNameFallback=design.OrganizationNameFallback,HeaderTitleFallback=design.HeaderTitleFallback,HeaderSubtitleFallback=design.HeaderSubtitleFallback,LabelContext=design.LabelContext,ChangeSummary=summary};
    private static DynamicParameters Parameters(LabelCanvasSaveRequest request,Guid tenantId,Guid userId,Guid id){var p=new DynamicParameters(request);p.Add("id",id,DbType.Guid);p.Add("tenantId",tenantId,DbType.Guid);p.Add("userId",userId,DbType.Guid);return p;}
    private static void ValidateIdentity(Guid tenantId,Guid userId){if(tenantId==Guid.Empty||userId==Guid.Empty)throw new InvalidOperationException("Tenant e usuário autenticado são obrigatórios.");}
    private static void ValidateRequest(LabelCanvasSaveRequest request){ArgumentNullException.ThrowIfNull(request);if(!Regex.IsMatch(request.TemplateKey??"","^[A-Za-z0-9_]{3,120}$"))throw new ArgumentException("A chave deve conter apenas letras, números e sublinhado.");if(string.IsNullOrWhiteSpace(request.TemplateName)||request.TemplateName.Length>200)throw new ArgumentException("Informe um nome de até 200 caracteres.");if(request.WidthMm is <=0 or >1000||request.HeightMm is <=0 or >1000)throw new ArgumentException("Dimensões inválidas.");if(string.IsNullOrWhiteSpace(request.SubjectType))throw new ArgumentException("Tipo de assunto obrigatório.");}
    private static InvalidOperationException ValidationException(LabelCanvasValidationResult validation)=>new("Corrija os erros críticos: "+string.Join("; ",validation.Issues.Where(x=>x.Severity=="ERROR").Take(5).Select(x=>x.Message)));
    private sealed record VersionSnapshot(string DesignJson,int VersionNo);
    private static async Task InsertEventAsync(System.Data.Common.DbConnection db,System.Data.Common.DbTransaction? tx,Guid tenantId,Guid? userId,Guid id,string eventType,string? message,object? payload,string? ipAddress,string? userAgent,CancellationToken ct)
    {const string sql="insert into ged.label_template_design_event(id,tenant_id,template_design_id,event_type,event_message,payload_json,created_by,created_at,ip_address,user_agent) values(gen_random_uuid(),@tenantId,@id,@eventType,@message,cast(@payload as jsonb),@userId,now(),@ipAddress,@userAgent)";var payloadJson=payload is null?null:JsonSerializer.Serialize(payload,new JsonSerializerOptions(JsonSerializerDefaults.Web));await db.ExecuteAsync(new CommandDefinition(sql,new{tenantId,id,eventType,message,payload=payloadJson,userId,ipAddress,userAgent},tx,cancellationToken:ct));}
}
