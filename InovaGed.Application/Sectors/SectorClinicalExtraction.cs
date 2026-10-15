using System.Text.Json;
using System.Text.RegularExpressions;

namespace InovaGed.Application.Sectors;

public sealed record SectorClinicalExtractionResult(int Status,string State,string Message,Guid? RunId=null,int Items=0);
public interface ISectorClinicalExtractionService
{
    Task<SectorClinicalExtractionResult> ExtractAsync(Guid tenantId,Guid userId,Guid linkId,string? idempotencyKey,CancellationToken ct,Guid? resumeRunId=null);
}

public sealed record SectorClinicalItem(string Category,string Name,string Dose,string Unit,string Route,string Frequency,
    string Quantity,string Date,string Context,string Evidence,int? Page)
{
    public string DisplayValue => string.Join(" · ",new[] { Name,Label("Dose",Dose),Label("Unidade",Unit),Label("Via",Route),
        Label("Frequência",Frequency),Label("Quantidade",Quantity),Label("Data",Date) }.Where(x=>x.Length>0));
    private static string Label(string name,string value)=>value.Length==0?"":name+": "+value;
}

public static class SectorClinicalEvidence
{
    public const string Mechanism="IA_CLINICA_V1";
    public static readonly string[] SupportedCatalog=["DS08","DS10","DS11","DS12","DS20","DS21","DS23","DS24"];
    public static string Fingerprint(Guid linkId,Guid documentId,Guid versionId,Guid encounterId,string catalog,string source,string? text)
        =>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { linkId,documentId,versionId,encounterId,catalog,source,text,mechanism=Mechanism }))));
    private static readonly string[] Keys=["category","name","dose","unit","route","frequency","quantity","date","context","evidence"];
    private static readonly string[] Categories=["medicamento","procedimento","material","opme"];
    private static readonly string[] Contexts=["PLANEJADO","PRESCRITO","MENCIONADO","DESCRITO_COMO_REALIZADO","INFORMACAO_INSUFICIENTE","NEGADO","CANCELADO"];
    public static JsonDocument Schema()
    {
        var properties=Keys.ToDictionary(x=>x,x=>(object)new { type="string",maxLength=x=="evidence"?2000:200 });
        properties["category"]=new { type="string",@enum=Categories };
        properties["context"]=new { type="string",@enum=Contexts };
        return JsonDocument.Parse(JsonSerializer.Serialize(new { type="object",additionalProperties=false,required=new[]{"items"},
            properties=new { items=new { type="array",maxItems=100,items=new { type="object",additionalProperties=false,required=Keys,properties } } } }));
    }

    // The provider may propose values, but only literal, version-bound evidence is accepted.
    public static bool TryRead(JsonElement output,string text,string catalog,out IReadOnlyList<SectorClinicalItem> items)
    {
        items=[];
        if(output.ValueKind!=JsonValueKind.Object || output.EnumerateObject().Count()!=1
            || !output.TryGetProperty("items",out var array) || array.ValueKind!=JsonValueKind.Array || array.GetArrayLength()>100) return false;
        var result=new List<SectorClinicalItem>();
        var identities=new HashSet<string>(StringComparer.Ordinal);
        foreach(var element in array.EnumerateArray())
        {
            if(element.ValueKind!=JsonValueKind.Object) return false;
            var fields=element.EnumerateObject().ToArray();
            if(fields.Length!=Keys.Length || fields.Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=Keys.Length
                || fields.Any(x=>!Keys.Contains(x.Name,StringComparer.Ordinal)||x.Value.ValueKind!=JsonValueKind.String)) return false;
            var values=fields.ToDictionary(x=>x.Name,x=>x.Value.GetString()!.Trim(),StringComparer.Ordinal);
            var evidence=values["evidence"];var context=values["context"];
            if(!Categories.Contains(values["category"],StringComparer.Ordinal)||!Contexts.Contains(context,StringComparer.Ordinal)
                || values["name"].Length==0 || evidence.Length==0 || evidence.Length>2000
                || values.Any(x=>x.Key!="evidence"&&x.Value.Length>200)) return false;
            var index=text.IndexOf(evidence,StringComparison.Ordinal);
            if(index<0) return false;
            foreach(var key in new[]{"name","dose","unit","route","frequency","quantity","date"})
                if(values[key].Length>0 && !evidence.Contains(values[key],StringComparison.OrdinalIgnoreCase)) return false;
            if(values["quantity"].Length>0 && values["quantity"].All(c=>char.IsDigit(c)||c is '.' or ','))
            {
                var pattern=@"\b(?:quantidade|qtd|qtde)\s*[:=]?\s*"+Regex.Escape(values["quantity"])+@"(?!\d|[.,]\d)";
                if(!Regex.IsMatch(evidence,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))) return false;
            }
            var negated=SectorExtraction.Read(evidence,catalog).Any(x=>x.Context==SectorExtraction.Negado);
            if(negated && context is not ("NEGADO" or "INFORMACAO_INSUFICIENTE")) return false;
            if(context=="NEGADO"&&!negated) return false;
            if(context=="CANCELADO"&&!Regex.IsMatch(evidence,@"\b(cancelad\p{L}*|suspens\p{L}*)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))) return false;
            if(context=="PRESCRITO"&&catalog is not ("DS23" or "DS24")&&!evidence.Contains("prescrit",StringComparison.OrdinalIgnoreCase)) return false;
            if(context=="PLANEJADO"&&!Regex.IsMatch(evidence,@"\b(planejad\p{L}*|previst\p{L}*|agendad\p{L}*|programad\p{L}*|solicitad\p{L}*|será|sera)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))) return false;
            if(context=="DESCRITO_COMO_REALIZADO" && (catalog is "DS23" or "DS24"
                || !(evidence.Contains("realizad",StringComparison.OrdinalIgnoreCase)||evidence.Contains("administrad",StringComparison.OrdinalIgnoreCase))
                || Regex.IsMatch(evidence,@"\b(será|sera|planejad\p{L}*|previst\p{L}*|agendad\p{L}*|cancelad\p{L}*|suspens\p{L}*)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))) return false;
            var page=text.IndexOf(evidence,index+evidence.Length,StringComparison.Ordinal)>=0?null:SectorTextLocator.PageAt(text,index);
            var item=new SectorClinicalItem(values["category"],values["name"],values["dose"],values["unit"],values["route"],values["frequency"],values["quantity"],values["date"],context,evidence,page);
            if(identities.Add(JsonSerializer.Serialize(item))) result.Add(item);
        }
        items=result;
        return true;
    }
}
