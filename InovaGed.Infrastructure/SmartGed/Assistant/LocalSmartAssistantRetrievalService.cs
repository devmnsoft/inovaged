using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SmartGed.Assistant;

namespace InovaGed.Infrastructure.SmartGed.Assistant;

public sealed class LocalSmartAssistantRetrievalService(IDbConnectionFactory db,ISmartAssistantIntentResolver intents,ISmartAssistantEvidenceRanker ranker) : ISmartAssistantRetrievalService
{
 private static readonly ConcurrentDictionary<string,(DateTimeOffset Expires,HashSet<string> Columns)> SchemaCache=new(StringComparer.OrdinalIgnoreCase);
 private static readonly Source[] Sources=
 [
  new("document","DOCUMENT",["code","title","name","process_number","protocol_number"],"/Documents/Details/",null,["FIND_DOCUMENT","DOCUMENT_SUMMARY","GENERAL_GED_SEARCH"]),
  new("document_ai_analysis","AI_ANALYSIS",["extracted_summary","detected_subject","detected_document_type","extracted_text"],"/SmartGed/Document/","document_id",["DOCUMENT_SUMMARY","OCR_STATUS","FIND_DOCUMENT"]),
  new("document_classification_suggestion","CLASSIFICATION",["suggested_classification_code","suggested_classification_title","suggested_reason","status"],"/SmartGed/Document/","document_id",["CLASSIFICATION_STATUS","FIND_DOCUMENT"]),
  new("document_retention_suggestion","RETENTION_RULE",["suggested_phase","suggested_final_destination","suggested_trigger_event","suggested_reason","status"],"/SmartGed/Document/","document_id",["RETENTION_STATUS","FIND_DOCUMENT"]),
  new("document_quality_issue","QUALITY_ISSUE",["title","issue_type","recommended_action","status"],"/SmartGed/Document/","document_id",["QUALITY_STATUS","FIND_DOCUMENT"]),
  new("box","BOX",["label_code","box_no","code","location_code","description"],"/PhysicalArchive/Boxes/",null,["FIND_BOX","GENERAL_GED_SEARCH"]),
  new("physical_location","LOCATION",["code","name","building","room","description"],"/PhysicalArchive/Locations/",null,["FIND_BOX"]),
  new("label_template_design","LABEL_DESIGN",["template_key","template_name","description","status","label_context","branding_binding_key"],"/Labels/Designer/Edit/",null,["LABEL_DESIGN","LABEL_TEMPLATE","LABEL_BRANDING","CLIENT_PROFILE"]),
  new("label_template_design_version","LABEL_VERSION",["status","change_summary","notes","snapshot_hash"],"/Labels/Designer",null,["LABEL_DESIGN","LABEL_TEMPLATE"]),
  new("label_print_history","LABEL_PRINT",["template_code","label_subject_type","reprint_reason","snapshot_json"],"/Labels/History/",null,["LABEL_STATUS","LABEL_HISTORY","LABEL_REPRINTS","LABEL_TEMPLATE","LABEL_BRANDING"]),
  new("print_branding_profile","BRANDING_PROFILE",["profile_code","profile_name","client_name","contract_name","organization_name","header_title","status"],"/Administration/PrintBranding/Profiles/",null,["LABEL_BRANDING","CLIENT_PROFILE"]),
  new("print_branding_binding","BRANDING_BINDING",["binding_context","binding_key","enabled"],"/Administration/PrintBranding/Bindings",null,["LABEL_BRANDING","CLIENT_PROFILE","LABEL_TEMPLATE"]),
  new("classification_plan","CLASSIFICATION",["code","title","description"],null,null,["CLASSIFICATION_STATUS","GENERAL_GED_SEARCH"]),
  new("class_node","CLASSIFICATION",["code","title","description"],null,null,["CLASSIFICATION_STATUS"]),
  new("retention_rule","RETENTION_RULE",["code","title","description","final_destination"],null,null,["RETENTION_STATUS"])
 ];

 public async Task<SmartAssistantRetrievalResult> RetrieveAsync(SmartAssistantRetrievalQuery query,CancellationToken ct)
 {
  var watch=Stopwatch.StartNew();var intent=query.ResolvedIntent??intents.Resolve(query.Question,query.ConversationHistory??[]);
  await using var conn=await db.OpenAsync(ct);var evidence=new List<SmartAssistantEvidence>();var warnings=new List<string>();var words=Terms(query.Question,intent.Filters).ToArray();
  foreach(var source in Sources.Where(x=>x.Intents.Contains(intent.Intent,StringComparer.OrdinalIgnoreCase)).Take(5))
  {
   var columns=await Columns(conn,source.Table,ct);var searchable=source.Columns.Where(columns.Contains).ToArray();
   if(!columns.Contains("tenant_id")||!columns.Contains("id")||searchable.Length==0){warnings.Add($"Fonte ged.{source.Table} indisponível para consulta segura.");continue;}
   var title=searchable[0];var searchableText="concat_ws(' ',"+string.Join(',',searchable.Select(Quote))+")";var target=source.TargetId is not null&&columns.Contains(source.TargetId)?Quote(source.TargetId):"id";
   var active=columns.Contains("reg_status")?" and coalesce(reg_status::text,'A') in ('A','ACTIVE')":"";var predicates=words.Length==0?"true":string.Join(" or ",words.Select((_,i)=>$"{searchableText} ilike @p{i}"));
   var order=columns.Contains("updated_at")?"updated_at desc nulls last":columns.Contains("created_at")?"created_at desc nulls last":"id";
   var sql=$"select id as Id,{target} as TargetId,coalesce(nullif({Quote(title)}::text,''),'{source.Type}') as Title,left(regexp_replace({searchableText},'\\s+',' ','g'),@excerpt) as Excerpt from ged.{Quote(source.Table)} where tenant_id=@tenantId{active} and ({predicates}) order by {order} limit @limit";
   var p=new DynamicParameters(new{query.TenantId,excerpt=Math.Clamp(query.MaxExcerptCharacters,120,600),limit=Math.Clamp(query.Limit,1,8)});for(var i=0;i<words.Length;i++)p.Add($"p{i}",$"%{words[i]}%");
   try{var rows=await conn.QueryAsync<EvidenceRow>(new CommandDefinition(sql,p,cancellationToken:ct));evidence.AddRange(rows.Select(r=>new SmartAssistantEvidence(source.Type,r.TargetId,Mask(r.Title),Mask(r.Excerpt),Url(source,r.TargetId),55,new Dictionary<string,string>{{"fonte",source.Type},{"título",Mask(r.Title)},{"detalhes",Mask(r.Excerpt)}})));}
   catch(Exception){warnings.Add($"Fonte ged.{source.Table} não pôde ser consultada com o schema atual.");}
  }
  watch.Stop();return new(ranker.Rank(query.Question,evidence,query.Limit),warnings.Distinct().Take(4).ToArray(),intent.Intent,watch.ElapsedMilliseconds);
 }

 private static async Task<HashSet<string>> Columns(System.Data.Common.DbConnection conn,string table,CancellationToken ct)
 {if(SchemaCache.TryGetValue(table,out var hit)&&hit.Expires>DateTimeOffset.UtcNow)return hit.Columns;var columns=(await conn.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name=@table",new{table},cancellationToken:ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);SchemaCache[table]=(DateTimeOffset.UtcNow.AddMinutes(5),columns);return columns;}
 private static IEnumerable<string> Terms(string question,IReadOnlyDictionary<string,string> filters){var filtered=filters.Values.Concat(Regex.Matches(question.ToLowerInvariant(),@"[\p{L}\p{N}][\p{L}\p{N}_./-]+") .Select(x=>x.Value)).Where(x=>x.Length>2&&!StopWords.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();return filtered.Length==0?[question.Trim()]:filtered;}
 private static string? Url(Source source,Guid? id)=>source.Url is null?null:source.Url.EndsWith('/')&&id.HasValue?source.Url+id:source.Url;
 private static string Quote(string value)=>$"\"{value.Replace("\"","\"\"")}\"";
 public static string Mask(string? value)=>Regex.Replace(Regex.Replace(value??"",@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b","***.***.***-**"),@"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b","**.***.***/****-**");
 private static readonly HashSet<string> StopWords=["quais","qual","onde","estao","está","esta","documentos","documento","sobre","para","com","dos","das","uma","que","foram","ainda","status","situação"];
 private sealed record Source(string Table,string Type,string[] Columns,string? Url,string? TargetId,string[] Intents);
 private sealed class EvidenceRow{public Guid Id{get;set;}public Guid? TargetId{get;set;}public string Title{get;set;}="";public string Excerpt{get;set;}="";}
}
