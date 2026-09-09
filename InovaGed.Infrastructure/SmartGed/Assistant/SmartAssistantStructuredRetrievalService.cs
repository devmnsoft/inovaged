using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SmartGed.Assistant;

namespace InovaGed.Infrastructure.SmartGed.Assistant;

public sealed class SmartAssistantStructuredRetrievalService(IDbConnectionFactory dbFactory) : ISmartAssistantStructuredRetrievalService
{
 public async Task<SmartAssistantStructuredResult> RetrieveAsync(SmartAssistantStructuredQuery query,CancellationToken ct)
 {
  await using var db=await dbFactory.OpenAsync(ct);var warnings=new List<string>();
  try
  {
   return query.Intent.Intent switch
   {
    "LABEL_REPRINTS"=>await Reprints(db,query,warnings,ct),
    "LABEL_STATUS"=>await LabelStatus(db,query,warnings,ct),
    "LABEL_TEMPLATE" or "LABEL_DESIGN"=>await Templates(db,query,warnings,ct),
    "LABEL_BRANDING" or "CLIENT_PROFILE"=>await Branding(db,query,warnings,ct),
    "CLASSIFICATION_STATUS"=>await Classification(db,query,warnings,ct),
    "RETENTION_STATUS"=>await Retention(db,query,warnings,ct),
    "OCR_STATUS"=>await Ocr(db,query,warnings,ct),
    "QUALITY_STATUS"=>await Quality(db,query,warnings,ct),
    "FIND_BOX"=>await Boxes(db,query,warnings,ct),
    "FIND_DOCUMENT"=>await Documents(db,query,warnings,ct),
    _=>SmartAssistantStructuredResult.Empty
   };
  }
  catch(Exception){return new(new Dictionary<string,string>(),[],null,new Dictionary<string,int>(),["A consulta estruturada não está disponível no schema atual; a busca textual foi mantida como fallback."]);}
 }

 private static async Task<SmartAssistantStructuredResult> Reprints(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"label_print_history",ct))return Missing("label_print_history");var range=Range(q.Intent);var sql="select label_subject_type Label,count(*)::int Count from ged.label_print_history where tenant_id=@TenantId and nullif(btrim(reprint_reason),'') is not null and (@From is null or printed_at>=@From) and (@To is null or printed_at<@To) group by label_subject_type order by 2 desc";
  var groups=(await db.QueryAsync<GroupRow>(new CommandDefinition(sql,new{q.TenantId,range.From,range.To},cancellationToken:ct))).ToList();var total=groups.Sum(x=>x.Count);var top=await db.QueryFirstOrDefaultAsync<string>(new CommandDefinition("select template_code from ged.label_print_history where tenant_id=@TenantId and nullif(btrim(reprint_reason),'') is not null and (@From is null or printed_at>=@From) and (@To is null or printed_at<@To) group by template_code order by count(*) desc limit 1",new{q.TenantId,range.From,range.To},cancellationToken:ct));
  return new(new Dictionary<string,string>{{"topTemplate",top??""}},groups.Select(x=>new SmartAssistantStructuredRow("LABEL_PRINT",null,x.Label,new Dictionary<string,string>{{"count",x.Count.ToString()}},"/Labels/History")).ToList(),total,groups.ToDictionary(x=>x.Label,x=>x.Count,StringComparer.OrdinalIgnoreCase),warnings);
 }

 private static async Task<SmartAssistantStructuredResult> LabelStatus(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"label_print_history",ct))return Missing("label_print_history");var range=Range(q.Intent);var count=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from ged.label_print_history where tenant_id=@TenantId and (@From is null or printed_at>=@From) and (@To is null or printed_at<@To)",new{q.TenantId,range.From,range.To},cancellationToken:ct));return new(new Dictionary<string,string>(),[],count,new Dictionary<string,int>{{"impressões",count}},warnings);
 }

 private static async Task<SmartAssistantStructuredResult> Templates(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"label_template_design",ct))return Missing("label_template_design");q.Intent.Filters.TryGetValue("template",out var template);var status=q.Question.Contains("rascunho",StringComparison.OrdinalIgnoreCase)?"DRAFT":q.Question.Contains("publicad",StringComparison.OrdinalIgnoreCase)?"PUBLISHED":null;var onlyWithoutProfile=q.Question.Contains("sem perfil",StringComparison.OrdinalIgnoreCase);
  const string sql="""
select d.id Id,d.template_key Code,d.template_name Title,d.status Status,d.current_version Version,
       coalesce(p.profile_name,'') ProfileName,coalesce(p.client_name,'') ClientName,coalesce(p.contract_name,'') ContractName,
       p.id BrandingId,p.primary_logo_asset_id LogoId
from ged.label_template_design d
left join ged.print_branding_binding b on b.tenant_id=@TenantId and b.binding_context='LABEL_TEMPLATE' and b.binding_key=d.template_key and b.enabled and b.reg_status='A'
left join ged.print_branding_profile p on p.tenant_id=@TenantId and p.id=coalesce(d.default_branding_profile_id,b.profile_id) and p.status='ACTIVE' and p.reg_status='A'
where (d.tenant_id=@TenantId or d.tenant_id is null) and d.reg_status in ('A','ACTIVE') and (@Template is null or upper(d.template_key)=upper(@Template)) and (@Status is null or d.status=@Status) and (not @OnlyWithoutProfile or p.id is null)
order by d.template_key,d.tenant_id nulls last,d.updated_at desc nulls last limit @Limit
""";
  var rows=(await db.QueryAsync<TemplateRow>(new CommandDefinition(sql,new{q.TenantId,Template=template,Status=status,OnlyWithoutProfile=onlyWithoutProfile,Limit=Math.Clamp(q.Limit,1,20)},cancellationToken:ct))).ToList();
  return new(new Dictionary<string,string>{{"status",status??"TODOS"}},rows.Select(x=>new SmartAssistantStructuredRow("LABEL_DESIGN",x.Id,x.Title,new Dictionary<string,string>{{"template",x.Code},{"status",x.Status},{"version",x.Version.ToString()},{"profile",x.ProfileName},{"client",x.ClientName},{"contract",x.ContractName},{"brandingProfileId",x.BrandingId?.ToString()??""},{"logoId",x.LogoId?.ToString()??""}},$"/Labels/Designer/Edit/{Uri.EscapeDataString(x.Code)}")).ToList(),rows.Count,rows.GroupBy(x=>x.Status).ToDictionary(x=>x.Key,x=>x.Count()),warnings);
 }

 private static async Task<SmartAssistantStructuredResult> Branding(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"print_branding_profile",ct))return Missing("print_branding_profile");q.Intent.Filters.TryGetValue("template",out var template);q.Intent.Filters.TryGetValue("client",out var client);q.Intent.Filters.TryGetValue("contract",out var contract);
  const string sql="""
select p.id Id,p.profile_code Code,p.profile_name Title,p.client_name ClientName,p.contract_name ContractName,p.organization_name OrganizationName,p.primary_logo_asset_id LogoId,a.asset_name LogoName,d.template_key TemplateKey
from ged.print_branding_profile p
left join ged.brand_asset a on a.tenant_id=p.tenant_id and a.id=p.primary_logo_asset_id
left join ged.print_branding_binding b on b.tenant_id=p.tenant_id and b.profile_id=p.id and b.binding_context='LABEL_TEMPLATE' and b.enabled and b.reg_status='A'
left join ged.label_template_design d on (d.tenant_id=@TenantId or d.tenant_id is null) and (d.default_branding_profile_id=p.id or d.template_key=b.binding_key)
where p.tenant_id=@TenantId and p.status='ACTIVE' and p.reg_status='A' and (@Template is null or d.template_key=@Template) and (@Client is null or p.client_name ilike '%'||@Client||'%') and (@Contract is null or p.contract_name ilike '%'||@Contract||'%')
order by p.is_default desc,p.profile_name limit @Limit
""";
  var rows=(await db.QueryAsync<BrandingRow>(new CommandDefinition(sql,new{q.TenantId,Template=template,Client=client,Contract=contract,Limit=Math.Clamp(q.Limit,1,20)},cancellationToken:ct))).DistinctBy(x=>x.Id).ToList();
  return new(new Dictionary<string,string>(),rows.Select(x=>new SmartAssistantStructuredRow("BRANDING_PROFILE",x.Id,x.Title,new Dictionary<string,string>{{"profile",x.Title},{"client",x.ClientName??""},{"contract",x.ContractName??""},{"organization",x.OrganizationName??""},{"logo",x.LogoName??""},{"template",x.TemplateKey??""}},$"/Administration/PrintBranding/Profiles/{x.Id}")).ToList(),rows.Count,new Dictionary<string,int>(),warnings);
 }

 private static async Task<SmartAssistantStructuredResult> Classification(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"document",ct))return Missing("document");var count=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from ged.document d where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and nullif(to_jsonb(d)->>'classification_id','') is null",new{q.TenantId},cancellationToken:ct));return new(new Dictionary<string,string>(),[],count,new Dictionary<string,int>{{"sem classificação",count}},warnings);
 }

 private static async Task<SmartAssistantStructuredResult> Ocr(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"document",ct))return Missing("document");var hasSearch=await Has(db,"document_search",ct);var count=hasSearch?await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*)::int from ged.document d left join ged.document_search s on s.tenant_id=d.tenant_id and s.document_id=d.id and nullif(btrim(s.ocr_text),'') is not null where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and s.document_id is null",new{q.TenantId},cancellationToken:ct)):0;return new(new Dictionary<string,string>(),[],count,new Dictionary<string,int>{{"OCR pendente",count}},warnings);
 }

 private static async Task<SmartAssistantStructuredResult> Retention(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"document_retention_suggestion",ct))return Missing("document_retention_suggestion");var rows=(await db.QueryAsync<BasicRow>(new CommandDefinition("select r.id Id,r.document_id SourceId,coalesce(d.title,d.code,'Documento') Title,concat_ws(' · ',r.suggested_phase,r.suggested_final_destination,r.status) Detail from ged.document_retention_suggestion r join ged.document d on d.tenant_id=r.tenant_id and d.id=r.document_id where r.tenant_id=@TenantId and coalesce(r.reg_status,'A')='A' and upper(coalesce(r.status,'')) in ('PENDING','OPEN','APPROACHING') order by r.created_at desc limit @Limit",new{q.TenantId,Limit=Math.Clamp(q.Limit,1,20)},cancellationToken:ct))).ToList();return BasicResult(rows,"RETENTION_RULE",warnings);
 }

 private static async Task<SmartAssistantStructuredResult> Quality(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"document_quality_result",ct))return Missing("document_quality_result");var rows=(await db.QueryAsync<BasicRow>(new CommandDefinition("select q.id Id,q.document_id SourceId,coalesce(d.title,d.code,'Documento') Title,concat_ws(' · ',q.quality_status,case when q.has_ocr then 'OCR' else 'sem OCR' end) Detail from ged.document_quality_result q left join ged.document d on d.tenant_id=q.tenant_id and d.id=q.document_id where q.tenant_id=@TenantId and (q.quality_status in ('Crítico','Atenção') or not q.has_ocr or not q.has_classification) order by q.analyzed_at_utc desc limit @Limit",new{q.TenantId,Limit=Math.Clamp(q.Limit,1,20)},cancellationToken:ct))).ToList();return BasicResult(rows,"QUALITY_STATUS",warnings);
 }

 private static async Task<SmartAssistantStructuredResult> Boxes(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"box",ct))return Missing("box");q.Intent.Filters.TryGetValue("boxCode",out var code);if(string.IsNullOrWhiteSpace(code))q.Intent.Filters.TryGetValue("identifier",out code);var rows=(await db.QueryAsync<BoxRow>(new CommandDefinition("select b.id Id,coalesce(b.label_code,b.box_no::text) Code,coalesce(b.notes,'Caixa física') Title,coalesce(to_jsonb(pl)->>'location_code',to_jsonb(pl)->>'name','') Location from ged.box b left join ged.physical_location pl on pl.tenant_id=b.tenant_id and pl.id=b.location_id where b.tenant_id=@TenantId and coalesce(b.reg_status,'A')='A' and (@Code is null or upper(coalesce(b.label_code,b.box_no::text))=upper(@Code)) order by b.created_at desc limit @Limit",new{q.TenantId,Code=code,Limit=Math.Clamp(q.Limit,1,20)},cancellationToken:ct))).ToList();return new(new Dictionary<string,string>(),rows.Select(x=>new SmartAssistantStructuredRow("BOX",x.Id,$"{x.Code} — {x.Title}",new Dictionary<string,string>{{"boxCode",x.Code},{"location",x.Location}},$"/PhysicalArchive/Boxes/{x.Id}")).ToList(),rows.Count,new Dictionary<string,int>(),warnings);
 }

 private static async Task<SmartAssistantStructuredResult> Documents(System.Data.Common.DbConnection db,SmartAssistantStructuredQuery q,List<string> warnings,CancellationToken ct)
 {
  if(!await Has(db,"document",ct))return Missing("document");q.Intent.Filters.TryGetValue("identifier",out var identifier);q.Intent.Filters.TryGetValue("boxCode",out var boxCode);Guid? boxId=q.Intent.Filters.TryGetValue("boxId",out var rawBox)&&Guid.TryParse(rawBox,out var parsed)?parsed:null;
  const string sql="""
select distinct d.id Id,d.id SourceId,coalesce(d.title,d.code,'Documento') Title,concat_ws(' · ',d.code,coalesce(b.label_code,b.box_no::text),coalesce(pl.location_code,'')) Detail
from ged.document d left join ged.batch_item bi on bi.tenant_id=d.tenant_id and bi.document_id=d.id and coalesce(bi.reg_status,'A')='A'
left join ged.box b on b.tenant_id=bi.tenant_id and b.id=bi.box_id left join ged.physical_location pl on pl.tenant_id=b.tenant_id and pl.id=b.location_id
where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and (@Identifier is null or upper(coalesce(d.code,d.id::text))=upper(@Identifier)) and (@BoxId is null or b.id=@BoxId) and (@BoxCode is null or upper(coalesce(b.label_code,b.box_no::text))=upper(@BoxCode))
order by d.id limit @Limit
""";var rows=(await db.QueryAsync<BasicRow>(new CommandDefinition(sql,new{q.TenantId,Identifier=identifier,BoxId=boxId,BoxCode=boxCode,Limit=Math.Clamp(q.Limit,1,20)},cancellationToken:ct))).ToList();return BasicResult(rows,"DOCUMENT",warnings);
 }

 private static SmartAssistantStructuredResult BasicResult(List<BasicRow> rows,string type,List<string> warnings)=>new(new Dictionary<string,string>(),rows.Select(x=>new SmartAssistantStructuredRow(type,x.SourceId,x.Title,new Dictionary<string,string>{{"details",x.Detail??""}},x.SourceId is Guid id?$"/Documents/Details/{id}":null)).ToList(),rows.Count,new Dictionary<string,int>(),warnings);
 private static SmartAssistantStructuredResult Missing(string table)=>new(new Dictionary<string,string>(),[],null,new Dictionary<string,int>(),[$"Fonte ged.{table} indisponível no schema atual."]);
 private static async Task<bool> Has(System.Data.Common.DbConnection db,string table,CancellationToken ct)=>await db.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.'||@table) is not null",new{table},cancellationToken:ct));
 private static (DateTime? From,DateTime? To) Range(SmartAssistantIntentResult intent){DateTime? from=intent.Filters.TryGetValue("dateFrom",out var a)&&DateTime.TryParse(a,out var av)?DateTime.SpecifyKind(av,DateTimeKind.Local):null;DateTime? to=intent.Filters.TryGetValue("dateTo",out var b)&&DateTime.TryParse(b,out var bv)?DateTime.SpecifyKind(bv,DateTimeKind.Local):null;return(from,to);}
 private sealed class GroupRow{public string Label{get;set;}="";public int Count{get;set;}}private sealed class BasicRow{public Guid Id{get;set;}public Guid? SourceId{get;set;}public string Title{get;set;}="";public string? Detail{get;set;}}
 private sealed class TemplateRow{public Guid Id{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public string Status{get;set;}="";public int Version{get;set;}public string ProfileName{get;set;}="";public string ClientName{get;set;}="";public string ContractName{get;set;}="";public Guid? BrandingId{get;set;}public Guid? LogoId{get;set;}}
 private sealed class BrandingRow{public Guid Id{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public string? ClientName{get;set;}public string? ContractName{get;set;}public string? OrganizationName{get;set;}public Guid? LogoId{get;set;}public string? LogoName{get;set;}public string? TemplateKey{get;set;}}
 private sealed class BoxRow{public Guid Id{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public string Location{get;set;}="";}
}
