using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasComponentPresetService(IDbConnectionFactory dbFactory, ILabelCanvasSchemaCapabilities capabilities)
    : ILabelCanvasComponentPresetService
{
    private const int MaxElements = 100;
    private static readonly HashSet<string> RuntimeProperties = new(StringComparer.OrdinalIgnoreCase)
        { "subjectId", "resolvedValue", "sampleValue", "tenantId", "userId", "traceCode", "databaseId", "filePath", "externalUrl", "assetId" };

    public async Task<IReadOnlyList<LabelCanvasComponentPresetDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        if (!(await capabilities.GetAsync(cancellationToken)).HasComponentPreset) return [];
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        const string sql = """
select id Id,name Name,category Category,schema_version SchemaVersion,jsonb_array_length(elements_json) ElementCount,
       created_at CreatedAt,updated_at UpdatedAt
from ged.label_canvas_component_preset where tenant_id=@tenantId and reg_status='A' order by updated_at desc nulls last,created_at desc
""";
        return (await db.QueryAsync<LabelCanvasComponentPresetDto>(new CommandDefinition(sql, new { tenantId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<LabelCanvasComponentPresetDetailsDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        if (!(await capabilities.GetAsync(cancellationToken)).HasComponentPreset) return null;
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        return await db.QuerySingleOrDefaultAsync<LabelCanvasComponentPresetDetailsDto>(new CommandDefinition(DetailsSql + " and id=@id", new { tenantId, id }, cancellationToken: cancellationToken));
    }

    public async Task<LabelCanvasComponentPresetDetailsDto> CreateAsync(Guid tenantId, Guid userId, LabelCanvasComponentPresetCreateRequest request, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId); EnsureUser(userId);
        if (!(await capabilities.GetAsync(cancellationToken)).HasComponentPreset) throw new LabelSchemaUpdateRequiredException();
        var name = ValidName(request.Name); var category = ValidCategory(request.Category);
        var elements = Normalize(request.ElementsJson, out var count);
        var id = Guid.NewGuid();
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        const string sql = """
insert into ged.label_canvas_component_preset(id,tenant_id,name,category,schema_version,elements_json,created_by)
values(@id,@tenantId,@name,@category,@schemaVersion,cast(@elements as jsonb),@userId)
""";
        await db.ExecuteAsync(new CommandDefinition(sql, new { id, tenantId, name, category, schemaVersion=request.SchemaVersion, elements, userId }, cancellationToken: cancellationToken));
        return new(id,name,category,request.SchemaVersion,count,DateTime.UtcNow,null,elements);
    }

    public async Task<LabelCanvasComponentPresetDto> RenameAsync(Guid tenantId, Guid userId, Guid id, string name, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId); EnsureUser(userId); name=ValidName(name);
        await using var db=await dbFactory.OpenAsync(cancellationToken);
        const string sql="""
update ged.label_canvas_component_preset set name=@name,updated_by=@userId,updated_at=now()
where tenant_id=@tenantId and id=@id and reg_status='A'
returning id Id,name Name,category Category,schema_version SchemaVersion,jsonb_array_length(elements_json) ElementCount,created_at CreatedAt,updated_at UpdatedAt
""";
        return await db.QuerySingleOrDefaultAsync<LabelCanvasComponentPresetDto>(new CommandDefinition(sql,new{tenantId,userId,id,name},cancellationToken:cancellationToken))
            ?? throw new KeyNotFoundException("Bloco não encontrado.");
    }

    public async Task<LabelCanvasComponentPresetDetailsDto> DuplicateAsync(Guid tenantId, Guid userId, Guid id, string? name = null, CancellationToken cancellationToken = default)
    {
        var source=await GetAsync(tenantId,id,cancellationToken) ?? throw new KeyNotFoundException("Bloco não encontrado.");
        return await CreateAsync(tenantId,userId,new(string.IsNullOrWhiteSpace(name)?$"{source.Name} — cópia":name,source.Category,source.ElementsJson,source.SchemaVersion),cancellationToken);
    }

    public async Task ArchiveAsync(Guid tenantId, Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId); EnsureUser(userId); await using var db=await dbFactory.OpenAsync(cancellationToken);
        var affected=await db.ExecuteAsync(new CommandDefinition("update ged.label_canvas_component_preset set reg_status='I',archived_by=@userId,archived_at=now(),updated_by=@userId,updated_at=now() where tenant_id=@tenantId and id=@id and reg_status='A'",new{tenantId,userId,id},cancellationToken:cancellationToken));
        if(affected==0)throw new KeyNotFoundException("Bloco não encontrado.");
    }

    private const string DetailsSql="""
select id Id,name Name,category Category,schema_version SchemaVersion,jsonb_array_length(elements_json) ElementCount,
       created_at CreatedAt,updated_at UpdatedAt,elements_json::text ElementsJson
from ged.label_canvas_component_preset where tenant_id=@tenantId and reg_status='A'
""";
    private static string Normalize(string json,out int count)
    {
        JsonArray elements; try { elements=JsonNode.Parse(json) as JsonArray ?? throw new JsonException(); }
        catch(JsonException){throw new LabelCanvasRequestException("INVALID_PRESET","Os elementos do bloco são inválidos.");}
        count=elements.Count;if(count is <2 or >MaxElements)throw new LabelCanvasRequestException("INVALID_PRESET_COUNT","Selecione entre 2 e 100 elementos.");
        decimal minX=decimal.MaxValue,minY=decimal.MaxValue;
        foreach(var node in elements){if(node is not JsonObject element)throw new LabelCanvasRequestException("INVALID_PRESET","O bloco contém um elemento inválido.");Sanitize(element);minX=Math.Min(minX,Number(element,"xMm"));minY=Math.Min(minY,Number(element,"yMm"));}
        foreach(var element in elements.OfType<JsonObject>()){element["xMm"]=Number(element,"xMm")-minX;element["yMm"]=Number(element,"yMm")-minY;element.Remove("id");element.Remove("groupId");}
        return elements.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    private static void Sanitize(JsonObject value){foreach(var key in value.Select(x=>x.Key).ToArray()){var node=value[key];if(node is JsonValue scalar&&scalar.TryGetValue<string>(out var text)&&IsUnsafe(text))throw new LabelCanvasRequestException("UNSAFE_ASSET","O bloco contém uma referência externa não permitida.");if(RuntimeProperties.Contains(key)){value.Remove(key);continue;}if(node is JsonObject child)Sanitize(child);else if(node is JsonArray array)foreach(var item in array.OfType<JsonObject>())Sanitize(item);}}
    private static bool IsUnsafe(string value)=>Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https" or "file" or "javascript";
    private static decimal Number(JsonObject value,string key)=>value[key]?.GetValue<decimal>()??0;
    private static string ValidName(string value){value=(value??"").Trim();if(value.Length is <3 or >160)throw new LabelCanvasRequestException("INVALID_PRESET_NAME","O nome deve ter entre 3 e 160 caracteres.");return value;}
    private static string ValidCategory(string value){value=(value??"").Trim();if(value.Length is <1 or >80)throw new LabelCanvasRequestException("INVALID_PRESET_CATEGORY","A categoria deve ter até 80 caracteres.");return value;}
    private static void EnsureTenant(Guid id){if(id==Guid.Empty)throw new ArgumentException("Tenant obrigatório.",nameof(id));}
    private static void EnsureUser(Guid id){if(id==Guid.Empty)throw new ArgumentException("Usuário obrigatório.",nameof(id));}
}
