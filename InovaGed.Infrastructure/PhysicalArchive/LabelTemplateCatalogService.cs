using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.PhysicalArchive;
using Microsoft.Extensions.Logging;
namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelTemplateCatalogService(IDbConnectionFactory dbFactory, ILogger<LabelTemplateCatalogService> logger) : ILabelTemplateCatalogService
{
 private const string Migration="database/migrations/2026_08_label_template_designer.sql";
 private static readonly IReadOnlyList<LabelTemplateOption> MinimumCatalog = [
  new("FACTORY_BOX_V1","Padrão do Sistema - Caixa","FACTORY","BOX","Etiqueta padrão do InovaGED para caixas físicas.","BoxLabel","1",true,false,true,null,true),
  new("FACTORY_DOCUMENT_V1","Padrão do Sistema - Documento/Pasta","FACTORY","DOCUMENT","Etiqueta padrão do InovaGED para documentos e pastas.","DocumentLabel","1",true,false,true,null,true),
  new("LOCDESK_CAIXA_V1","LocDesk - Caixa","CUSTOM","BOX","Modelo personalizado LocDesk para identificação de caixas físicas.","LocDeskBoxLabel","1",true,true,false,null,true),
  new("LOCDESK_PASTA_V1","LocDesk - Pasta","CUSTOM","DOCUMENT","Modelo personalizado LocDesk para identificação de pastas/documentos.","LocDeskFolderLabel","1",true,true,false,null,true)];
 public bool IsTemporaryCatalog {get;private set;}

 public async Task<IReadOnlyList<LabelTemplateOption>> GetTemplatesAsync(Guid tenantId,string subjectType,string? mode,CancellationToken ct){
  await using var db=await dbFactory.OpenAsync(ct);var source=await GetSourceAsync(db,ct);var normalized=string.IsNullOrWhiteSpace(mode)?null:mode;
  if(source==CatalogSource.Template)return (await db.QueryAsync<LabelTemplateOption>(new CommandDefinition("""select code,name,print_mode Mode,subject_type SubjectType,coalesce(description,'') Description,view_name ViewName,version::text Version,supports_batch SupportsBatch,allows_manual_fields AllowsManualFields,is_system_template IsSystemTemplate,id,is_default IsDefault from ged.label_template where (tenant_id=@tenantId or tenant_id is null) and subject_type=@subjectType and (@mode is null or print_mode=@mode) and is_active and reg_status='A' order by is_default desc,display_order,name""",new{tenantId,subjectType,mode=normalized},cancellationToken:ct))).AsList();
  if(source==CatalogSource.Legacy)return (await db.QueryAsync<LabelTemplateOption>(new CommandDefinition("""select code,name,print_mode Mode,subject_type SubjectType,coalesce(description,'') Description,view_name ViewName,version Version,supports_batch SupportsBatch,allows_manual_fields AllowsManualFields,is_system_template IsSystemTemplate,id,false IsDefault from ged.label_template_catalog where subject_type=@subjectType and (@mode is null or print_mode=@mode) and is_active order by display_order,name""",new{subjectType,mode=normalized},cancellationToken:ct))).AsList();
  return MinimumCatalog.Where(x=>x.SubjectType==subjectType&&(normalized is null||x.Mode==normalized)).ToList();
 }
 public async Task<LabelTemplateOption> GetTemplateAsync(Guid tenantId,string templateCode,CancellationToken ct){
  await using var db=await dbFactory.OpenAsync(ct);var source=await GetSourceAsync(db,ct);
  if(source==CatalogSource.Template)return await db.QuerySingleOrDefaultAsync<LabelTemplateOption>(new CommandDefinition("""select code,name,print_mode Mode,subject_type SubjectType,coalesce(description,'') Description,view_name ViewName,version::text Version,supports_batch SupportsBatch,allows_manual_fields AllowsManualFields,is_system_template IsSystemTemplate,id,is_default IsDefault from ged.label_template where (tenant_id=@tenantId or tenant_id is null) and code=@templateCode and is_active and reg_status='A'""",new{tenantId,templateCode},cancellationToken:ct))??throw NotFound();
  if(source==CatalogSource.Legacy)return await db.QuerySingleOrDefaultAsync<LabelTemplateOption>(new CommandDefinition("""select code,name,print_mode Mode,subject_type SubjectType,coalesce(description,'') Description,view_name ViewName,version Version,supports_batch SupportsBatch,allows_manual_fields AllowsManualFields,is_system_template IsSystemTemplate,id,false IsDefault from ged.label_template_catalog where code=@templateCode and is_active""",new{templateCode},cancellationToken:ct))??throw NotFound();
  return MinimumCatalog.FirstOrDefault(x=>x.Code==templateCode)??throw NotFound();
 }
 public async Task<bool> IsCompatibleAsync(Guid tenantId,string templateCode,string subjectType,CancellationToken ct)=>(await GetTemplatesAsync(tenantId,subjectType,null,ct)).Any(x=>x.Code==templateCode);
 private async Task<CatalogSource> GetSourceAsync(System.Data.Common.DbConnection db,CancellationToken ct){var x=await db.QuerySingleAsync<CatalogTables>(new CommandDefinition("select to_regclass('ged.label_template') is not null as Template,to_regclass('ged.label_template_catalog') is not null as Legacy",cancellationToken:ct));IsTemporaryCatalog=!x.Template;if(x.Template)return CatalogSource.Template;logger.LogWarning("A migration {Migration} ainda não foi aplicada; usando {Fallback} para a Central de Etiquetas.",Migration,x.Legacy?"ged.label_template_catalog":"catálogo mínimo em memória");return x.Legacy?CatalogSource.Legacy:CatalogSource.Memory;}
 private static KeyNotFoundException NotFound()=>new("Modelo de etiqueta não encontrado.");
 private sealed record CatalogTables(bool Template,bool Legacy);private enum CatalogSource{Template,Legacy,Memory}
}
