using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class ManualLabelInstanceService(IDbConnectionFactory dbFactory,ILabelCanvasFieldCatalogService catalog) : IManualLabelInstanceService
{
    private static readonly HashSet<string> Automatic=new(StringComparer.OrdinalIgnoreCase){"traceCode","qrPayload","createdAt","printedBy","primaryLogo","secondaryLogo","clientName","contractName","organizationName"};
    public IReadOnlyList<LabelCanvasFieldDto> EditableFields(LabelCanvasDesignDto design)
    {
        var used=Document(design).Elements.Where(x=>x.Binding?.Field is {Length:>0}).Select(x=>x.Binding!.Field!).Where(x=>!Automatic.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return catalog.GetFields(design.SubjectType).Where(x=>used.Contains(x.Key)).ToArray();
    }
    public LabelCanvasValidationResult Validate(LabelCanvasDesignDto design,IReadOnlyDictionary<string,string?> values)
    {
        var result=new LabelCanvasValidationResult();var elements=Document(design).Elements.Where(x=>x.Binding?.Field is {Length:>0}).ToArray();var allowed=EditableFields(design).Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var key in values.Keys.Where(x=>!allowed.Contains(x)))result.Issues.Add(new("MANUAL_UNKNOWN_FIELD","ERROR",$"O campo '{key}' não pertence ao modelo."));
        foreach(var element in elements.Where(x=>allowed.Contains(x.Binding!.Field!)))
        { var value=values.GetValueOrDefault(element.Binding!.Field!);if(element.Validation.Required&&string.IsNullOrWhiteSpace(value))result.Issues.Add(new("MANUAL_REQUIRED","ERROR",$"Preencha {element.Name}.",element.Id));if(element.Validation.MaxCharacters is int max&&value?.Length>max)result.Issues.Add(new("MANUAL_MAX_LENGTH","ERROR",$"{element.Name} aceita no máximo {max} caracteres.",element.Id)); }
        return result;
    }
    public async Task<ManualLabelInstance> SaveDraftAsync(Guid tenantId,Guid userId,LabelCanvasDesignDto design,IReadOnlyDictionary<string,string?> values,Guid? brandingProfileId,CancellationToken cancellationToken=default)
    {
        if(tenantId==Guid.Empty||userId==Guid.Empty)throw new ArgumentException("Tenant e usuário são obrigatórios.");var validation=Validate(design,values);if(validation.HasErrors)throw new ArgumentException(string.Join(" ",validation.Issues.Select(x=>x.Message)));
        var id=Guid.NewGuid();await using var db=await dbFactory.OpenAsync(cancellationToken);const string sql="""
insert into ged.label_manual_instance(id,tenant_id,template_key,template_version,values_json,branding_profile_id,status,created_by,created_at,updated_by,updated_at,reg_status)
values(@id,@tenantId,@key,@version,cast(@json as jsonb),@branding,'DRAFT',@userId,now(),@userId,now(),'ACTIVE')
""";await db.ExecuteAsync(new CommandDefinition(sql,new{id,tenantId,key=design.TemplateKey,version=design.CurrentVersion,json=JsonSerializer.Serialize(values),branding=brandingProfileId,userId},cancellationToken:cancellationToken));return new(id,tenantId,design.TemplateKey,design.CurrentVersion,values,brandingProfileId,"DRAFT");
    }
    public async Task<ManualLabelInstance?> GetAsync(Guid tenantId,Guid id,CancellationToken cancellationToken=default)
    {await using var db=await dbFactory.OpenAsync(cancellationToken);var row=await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("select id,tenant_id TenantId,template_key TemplateKey,template_version TemplateVersion,values_json::text ValuesJson,branding_profile_id BrandingProfileId,status from ged.label_manual_instance where id=@id and tenant_id=@tenantId and reg_status='ACTIVE'",new{id,tenantId},cancellationToken:cancellationToken));return row is null?null:Map(row);}
    public async Task<ManualLabelInstance> DuplicateAsync(Guid tenantId,Guid userId,Guid id,CancellationToken cancellationToken=default)
    {var source=await GetAsync(tenantId,id,cancellationToken)??throw new KeyNotFoundException("Etiqueta avulsa não encontrada.");await using var db=await dbFactory.OpenAsync(cancellationToken);var newId=Guid.NewGuid();await db.ExecuteAsync(new CommandDefinition("insert into ged.label_manual_instance(id,tenant_id,template_key,template_version,values_json,branding_profile_id,status,created_by,created_at,updated_by,updated_at,reg_status) select @newId,tenant_id,template_key,template_version,values_json,branding_profile_id,'DRAFT',@userId,now(),@userId,now(),'ACTIVE' from ged.label_manual_instance where id=@id and tenant_id=@tenantId and reg_status='ACTIVE'",new{newId,id,tenantId,userId},cancellationToken:cancellationToken));return (await GetAsync(tenantId,newId,cancellationToken))!;}
    public async Task MarkPrintedAsync(Guid tenantId,Guid userId,Guid id,CancellationToken cancellationToken=default)
    {await using var db=await dbFactory.OpenAsync(cancellationToken);var changed=await db.ExecuteAsync(new CommandDefinition("update ged.label_manual_instance set status='PRINTED',updated_by=@userId,updated_at=now() where id=@id and tenant_id=@tenantId and reg_status='ACTIVE'",new{id,tenantId,userId},cancellationToken:cancellationToken));if(changed!=1)throw new KeyNotFoundException("Etiqueta avulsa não encontrada.");}
    private static LabelCanvasDocumentDto Document(LabelCanvasDesignDto design)=>JsonSerializer.Deserialize<LabelCanvasDocumentDto>(design.DesignJson,new JsonSerializerOptions(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true})??new();
    private static ManualLabelInstance Map(Row x)=>new(x.Id,x.TenantId,x.TemplateKey,x.TemplateVersion,JsonSerializer.Deserialize<Dictionary<string,string?>>(x.ValuesJson)??new(),x.BrandingProfileId,x.Status);
    private sealed class Row{public Guid Id{get;init;}public Guid TenantId{get;init;}public string TemplateKey{get;init;}="";public int TemplateVersion{get;init;}public string ValuesJson{get;init;}="{}";public Guid? BrandingProfileId{get;init;}public string Status{get;init;}="";}
}
