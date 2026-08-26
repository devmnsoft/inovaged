using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SmartGed.Assistant;

namespace InovaGed.Infrastructure.SmartGed.Assistant;

public sealed class LocalSmartAssistantRetrievalService(IDbConnectionFactory db) : ISmartAssistantRetrievalService
{
 private static readonly Source[] Sources =
 [
  new("document","DOCUMENT",["title","name","process_number","protocol_number"],"/Documents/Details/"),
  new("document_ai_analysis","AI_ANALYSIS",["extracted_text","extracted_summary","detected_subject","detected_document_type"],"/SmartGed/Document/", "document_id"),
  new("document_classification_suggestion","CLASSIFICATION",["suggested_classification_code","suggested_classification_title","suggested_reason","status"],"/SmartGed/Document/", "document_id"),
  new("document_retention_suggestion","RETENTION_RULE",["suggested_phase","suggested_final_destination","suggested_trigger_event","suggested_reason","status"],"/SmartGed/Document/", "document_id"),
  new("document_quality_issue","QUALITY_ISSUE",["title","issue_type","recommended_action","status"],"/SmartGed/Document/", "document_id"),
  new("physical_box","BOX",["code","box_code","location","physical_location","description"],"/Physical/Boxes"),
  new("locdesk_label_draft","LOCDESK_DRAFT",["control_number","label_data","qr_content","title","status"],null),
  new("label_print","LABEL_PRINT",["title","label_code","status","payload_json"],"/Labels/History"),
  new("label_print_history","LABEL_PRINT",["title","label_code","status","payload_json"],"/Labels/History"),
  new("classification_plan","CLASSIFICATION",["code","title","description"],null), new("class_node","CLASSIFICATION",["code","title","description"],null),
  new("retention_rule","RETENTION_RULE",["code","title","description","final_destination"],null)
 ];

 public async Task<SmartAssistantRetrievalResult> RetrieveAsync(SmartAssistantRetrievalQuery query, CancellationToken ct)
 {
  await using var conn=await db.OpenAsync(ct); var evidence=new List<SmartAssistantEvidence>(); var warnings=new List<string>();
  var words=Regex.Matches(query.Question.ToLowerInvariant(),@"[\p{L}\p{N}][\p{L}\p{N}_./-]+")
   .Select(x=>x.Value).Where(x=>x.Length>2 && !StopWords.Contains(x)).Distinct().Take(8).ToArray();
  var pattern="%"+string.Join("%",words)+"%"; if(words.Length==0) pattern="%"+query.Question.Trim()+"%";
  foreach(var source in Sources)
  {
   var columns=(await conn.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name=@table",new{table=source.Table},cancellationToken:ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
   if(columns.Count==0){warnings.Add($"Fonte ged.{source.Table} indisponível neste schema.");continue;}
   var searchable=source.Columns.Where(columns.Contains).ToArray();
   if(!columns.Contains("tenant_id")||!columns.Contains("id")||searchable.Length==0){warnings.Add($"Fonte ged.{source.Table} não possui as colunas necessárias para consulta segura.");continue;}
   var title=searchable.First(); var text="concat_ws(' ',"+string.Join(',',searchable.Select(Quote))+")";
   var target=source.TargetId is not null && columns.Contains(source.TargetId)?Quote(source.TargetId):"id";
   var active=columns.Contains("reg_status")?" and coalesce(reg_status,'A')='A'":"";
   var sql=$"select id as Id,{target} as TargetId,coalesce(nullif({Quote(title)}::text,''),'{source.Type}') as Title,left(regexp_replace({text},'\\s+',' ','g'),500) as Excerpt from ged.{Quote(source.Table)} where tenant_id=@tenantId{active} and {text} ilike @pattern order by id limit @limit";
   var rows=await conn.QueryAsync<EvidenceRow>(new CommandDefinition(sql,new{query.TenantId,pattern,limit=Math.Clamp(query.Limit-evidence.Count,1,20)},cancellationToken:ct));
   evidence.AddRange(rows.Select(r=>new SmartAssistantEvidence(source.Type,r.TargetId,Mask(r.Title),Mask(r.Excerpt),source.Url is null?null:source.Url+(source.Url.EndsWith('/')?r.TargetId:null),85)));
   if(evidence.Count>=query.Limit)break;
  }
  return new(evidence.Take(query.Limit).ToArray(),warnings);
 }
 private static string Quote(string value)=>$"\"{value.Replace("\"","\"\"")}\"";
 public static string Mask(string? value)=>Regex.Replace(Regex.Replace(value??"",@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b","***.***.***-**"),@"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b","**.***.***/****-**");
 private static readonly HashSet<string> StopWords=["quais","qual","onde","estao","está","esta","documentos","documento","sobre","para","com","dos","das","uma","que","foram","ainda"];
 private sealed record Source(string Table,string Type,string[] Columns,string? Url,string? TargetId=null);
 private sealed class EvidenceRow { public Guid Id{get;set;} public Guid? TargetId{get;set;} public string Title{get;set;}=""; public string Excerpt{get;set;}=""; }
}
