using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasValueResolver(IDbConnectionFactory dbFactory) : ILabelCanvasValueResolver
{
    public async Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid tenantId, string designerSubjectType, string operationalSubjectType, Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        var isBox = operationalSubjectType.Equals("BOX", StringComparison.OrdinalIgnoreCase)
            || designerSubjectType.Equals("Box", StringComparison.OrdinalIgnoreCase)
            || designerSubjectType.Equals("LocDeskBox", StringComparison.OrdinalIgnoreCase);
        var table=isBox?"box":"document";
        var entityJson=await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition($"select to_jsonb(e)::text from ged.{table} e where e.tenant_id=@tenantId and e.id=@subjectId and coalesce(to_jsonb(e)->>'reg_status','A') in ('A','ACTIVE') limit 1",new{tenantId,subjectId},cancellationToken:cancellationToken));
        if(string.IsNullOrWhiteSpace(entityJson))return new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase);
        var entity=Parse(entityJson);var folder=Empty;var classification=Empty;var location=Empty;var version=Empty;var box=Empty;string? documentCount=null;
        if(isBox)
        {
            location=await Related(db,"physical_location",tenantId,Id(entity,"location_id"),cancellationToken);
            if(await HasTable(db,"batch_item",cancellationToken))documentCount=await db.ExecuteScalarAsync<string?>(new CommandDefinition("select count(*)::text from ged.batch_item bi where bi.tenant_id=@tenantId and to_jsonb(bi)->>'box_id'=cast(@subjectId as text) and coalesce(to_jsonb(bi)->>'reg_status','A') in ('A','ACTIVE')",new{tenantId,subjectId},cancellationToken:cancellationToken));
        }
        else
        {
            folder=await Related(db,"folder",tenantId,Id(entity,"folder_id"),cancellationToken);classification=await Related(db,"classification_plan",tenantId,Id(entity,"classification_id"),cancellationToken);version=await Related(db,"document_version",tenantId,Id(entity,"current_version_id"),cancellationToken);
            if(await HasTable(db,"batch_item",cancellationToken))
            {
                var itemJson=await db.QueryFirstOrDefaultAsync<string>(new CommandDefinition("select to_jsonb(bi)::text from ged.batch_item bi where bi.tenant_id=@tenantId and to_jsonb(bi)->>'document_id'=cast(@subjectId as text) and coalesce(to_jsonb(bi)->>'reg_status','A') in ('A','ACTIVE') limit 1",new{tenantId,subjectId},cancellationToken:cancellationToken));
                var item=Parse(itemJson);box=await Related(db,"box",tenantId,Id(item,"box_id"),cancellationToken);location=await Related(db,"physical_location",tenantId,Id(box,"location_id"),cancellationToken);
            }
        }
        return ResolveValues(designerSubjectType,subjectId,entity,folder,classification,location,version,documentCount,box);
    }

    public static IReadOnlyDictionary<string, object?> ResolveValues(
        string designerSubjectType,
        Guid subjectId,
        IReadOnlyDictionary<string, object?> entity,
        IReadOnlyDictionary<string, object?>? folder = null,
        IReadOnlyDictionary<string, object?>? classification = null,
        IReadOnlyDictionary<string, object?>? location = null,
        IReadOnlyDictionary<string, object?>? version = null,
        string? documentCount = null,
        IReadOnlyDictionary<string, object?>? box = null)
    {
        folder ??= Empty;
        classification ??= Empty;
        location ??= Empty;
        version ??= Empty;
        box ??= Empty;
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var isBox = designerSubjectType.Equals("Box", StringComparison.OrdinalIgnoreCase) || designerSubjectType.Equals("LocDeskBox", StringComparison.OrdinalIgnoreCase);

        if (isBox)
        {
            result["boxCode"] = First(entity, "label_code", "box_code", "code");
            result["boxNumber"] = First(entity, "box_no", "number");
            result["controlNumber"] = result["boxNumber"];
            result["archiveTitle"] = First(entity, "title", "name", "notes");
            result["subject"] = First(entity, "title", "notes");
            result["sector"] = First(location, "sector", "name");
            result["classification"] = First(entity, "classification", "classification_code");
            result["periodStart"] = First(entity, "period_start", "start_date");
            result["periodEnd"] = First(entity, "period_end", "end_date");
            result["retentionStatus"] = First(entity, "retention_status", "retention_phase");
            result["custodyStatus"] = First(entity, "custody_status", "status");
            result["documentCount"] = documentCount;
        }
        else
        {
            var code = First(entity, "code", "document_code", "protocol_number");
            var title = First(entity, "title", "name", "subject");
            result["documentCode"] = code;
            result["controlNumber"] = code;
            result["documentTitle"] = title;
            result["subject"] = title;
            result["documentType"] = First(entity, "document_type", "type_name", "type");
            result["folderName"] = First(folder, "name", "title", "path");
            result["boxNumber"] = First(box, "box_no", "number");
            result["classification"] = JoinCodeTitle(classification) ?? First(entity, "classification", "classification_code");
            result["createdAt"] = First(entity, "created_at", "document_date");
            result["uploadedBy"] = First(entity, "uploaded_by_name", "created_by_name");
            result["ocrStatus"] = First(version, "ocr_status") ?? First(entity, "ocr_status");
            result["retentionStatus"] = First(entity, "retention_status", "retention_phase");
            result["processNumber"] = First(entity, "process_number", "protocol_number", "code");
            result["recordNumber"] = First(entity, "record_number", "medical_record_number", "code");
            result["patientName"] = First(entity, "patient_name", "person_name", "holder_name");
            result["documentPeriod"] = First(entity, "document_period", "period");
        }

        result["location"] = First(location, "location_code", "code", "name", "title") ?? First(entity, "location", "location_code");
        result["traceCode"] = First(entity, "trace_code", "label_code", "code") ?? subjectId.ToString("D");
        result["qrPayload"] = null;
        return result;
    }

    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>();

    private static async Task<IReadOnlyDictionary<string,object?>> Related(System.Data.Common.DbConnection db,string table,Guid tenantId,Guid? id,CancellationToken ct)
    {
        if(!id.HasValue||!await HasTable(db,table,ct))return Empty;
        var json=await db.QueryFirstOrDefaultAsync<string>(new CommandDefinition($"select to_jsonb(e)::text from ged.{table} e where e.tenant_id=@tenantId and e.id=@id limit 1",new{tenantId,id},cancellationToken:ct));return Parse(json);
    }
    private static Task<bool> HasTable(System.Data.Common.DbConnection db,string table,CancellationToken ct)=>db.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.'||@table) is not null",new{table},cancellationToken:ct));
    private static Guid? Id(IReadOnlyDictionary<string,object?> values,string key)=>values.TryGetValue(key,out var raw)&&Guid.TryParse(Convert.ToString(raw),out var id)?id:null;

    private static Dictionary<string, object?> Parse(string? json)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return values;
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
            values[property.Name] = Scalar(property.Value);
        if (document.RootElement.TryGetProperty("metadata", out var metadata))
        {
            if (metadata.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(metadata.GetString()))
                foreach (var item in Parse(metadata.GetString())) values.TryAdd(item.Key, item.Value);
            else if (metadata.ValueKind == JsonValueKind.Object)
                foreach (var item in metadata.EnumerateObject()) values.TryAdd(item.Name, Scalar(item.Value));
        }
        return values;
    }

    private static object? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };

    private static object? First(IReadOnlyDictionary<string, object?> values, params string[] keys) =>
        keys.Select(key => values.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(Convert.ToString(value)));

    private static string? JoinCodeTitle(IReadOnlyDictionary<string, object?> classification)
    {
        var code = Convert.ToString(First(classification, "code", "classification_code"));
        var title = Convert.ToString(First(classification, "title", "name", "description"));
        return string.IsNullOrWhiteSpace(code) ? title : string.IsNullOrWhiteSpace(title) ? code : $"{code} - {title}";
    }

}
