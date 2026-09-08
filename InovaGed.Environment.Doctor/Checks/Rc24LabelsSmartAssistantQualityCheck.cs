using Dapper;
using Npgsql;

namespace InovaGed.Environment.Doctor.Checks;

public static class Rc24LabelsSmartAssistantQualityCheck
{
 private static readonly string[] GenericTemplates=["CLIENTE_CAIXA_CANVAS_V1","CLIENTE_DOCUMENTO_CANVAS_V1","CLIENTE_PASTA_CANVAS_V1","CLIENTE_PRONTUARIO_CANVAS_V1","CLIENTE_PROCESSO_CANVAS_V1"];
 private static readonly string[] Forbidden=["LocDesk","HOL","Hospital Ophir Loyola","Hosp. Ophir Loyola","ARQUIVO LOCDESCK ANANINDEUA"];

 public static async Task<int> RunAsync(string root,string? connectionString,TextWriter output,TextWriter error,CancellationToken ct)
 {
  var failures=new List<string>();var warnings=new List<string>();
  void Check(bool ok,string name,string detail){if(ok)output.WriteLine($"[OK] {name}");else{failures.Add(name);error.WriteLine($"[FALHA] {name}: {detail}");}}
  var migration=Read(root,"database","migrations","2026_09_08_label_canvas_multi_client_rc23.sql");var labels=Read(root,"InovaGed.Web","Controller","LabelsController.cs");var retrieval=Read(root,"InovaGed.Infrastructure","SmartGed","Assistant","LocalSmartAssistantRetrievalService.cs");var service=Read(root,"InovaGed.Infrastructure","SmartGed","Assistant","SmartGedAssistantService.cs");var intent=Read(root,"InovaGed.Infrastructure","SmartGed","Assistant","LocalSmartAssistantIntentResolver.cs");var composer=Read(root,"InovaGed.Infrastructure","SmartGed","Assistant","LocalSmartAssistantAnswerComposer.cs");
  Check(GenericTemplates.All(migration.Contains),"Templates multi-cliente","Os cinco templates genéricos não estão presentes na migration.");
  var genericSeed=GenericTemplates.Select(key=>migration.IndexOf(key,StringComparison.Ordinal)).Where(x=>x>=0).DefaultIfEmpty(0).Min();var genericText=migration[genericSeed..];
  Check(!Forbidden.Any(genericText.Contains),"Conteúdo neutro","O bloco CLIENTE contém marca ou cliente legado.");
  Check(new[]{"default_branding_profile_id","branding_binding_key","client_name_fallback","contract_name_fallback","organization_name_fallback","label_context"}.All(migration.Contains),"Contrato Canvas RC23","Colunas multi-cliente ausentes.");
  Check(new[]{"PrintBrandingContext.LabelTemplate","DefaultBrandingProfileId","BrandingBindingKey","branding=new","primaryLogo","secondaryLogo"}.All(labels.Contains),"Resolução e snapshot de branding","Impressão Canvas não registra toda a identidade resolvida.");
  Check(new[]{"FIND_DOCUMENT","FIND_BOX","DOCUMENT_SUMMARY","CLASSIFICATION_STATUS","RETENTION_STATUS","OCR_STATUS","LABEL_REPRINTS","LABEL_BRANDING","CLIENT_PROFILE"}.All(intent.Contains),"Intents documentais","Resolver local não cobre os intents essenciais.");
  Check(retrieval.Contains("tenant_id=@tenantId")&&retrieval.Contains("SchemaCache")&&retrieval.Contains("Take(5)")&&retrieval.Contains("Math.Clamp(query.Limit,1,8)"),"Retrieval local e limitada","Busca não está claramente isolada, cacheada ou limitada.");
  Check(new[]{"label_template_design","label_print_history","print_branding_profile","print_branding_binding"}.All(retrieval.Contains),"Fontes de etiquetas","Retrieval não cobre design, impressão e branding.");
  Check(service.Contains("user_id=@UserId")&&service.Contains("s.user_id=@userId")&&service.Contains("resolvedIntent")&&service.Contains("retrievalDurationMs")&&service.Contains("sourceCount"),"Isolamento de sessão e metadados","Sessão/citações/ações não demonstram isolamento por usuário ou metadados operacionais.");
  Check(composer.Contains("INSUFFICIENT_EVIDENCE")&&composer.Contains("OPEN_LABEL_DESIGNER")&&composer.Contains("OPEN_LABEL_HISTORY")&&composer.Contains("OPEN_BRANDING_PROFILE"),"Síntese e ações seguras","Compositor não possui recusa segura e ações de navegação para etiquetas.");
  Check(retrieval.Contains("***.***.***-**")&&retrieval.Contains("**.***.***/****-**"),"Mascaramento LGPD","CPF/CNPJ não estão mascarados.");
  if(string.IsNullOrWhiteSpace(connectionString))warnings.Add("Banco não configurado; validação PostgreSQL foi omitida.");
  else try
  {
   await using var db=new NpgsqlConnection(connectionString);await db.OpenAsync(ct);
   var columns=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from information_schema.columns where table_schema='ged' and table_name='label_template_design' and column_name in ('default_branding_profile_id','branding_binding_key','client_name_fallback','contract_name_fallback','organization_name_fallback','header_title_fallback','header_subtitle_fallback','label_context')",cancellationToken:ct));Check(columns==8,"Schema RC23 aplicado","Colunas do Canvas multi-cliente estão pendentes.");
   var templates=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(distinct template_key) from ged.label_template_design where template_key=any(@keys) and label_context='GENERIC' and status='DRAFT' and not is_system_template",new{keys=GenericTemplates},cancellationToken:ct));Check(templates==GenericTemplates.Length,"Seeds RC23 no banco","Templates multi-cliente ausentes ou não editáveis.");
   var assistantTables=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from information_schema.tables where table_schema='ged' and table_name in ('smart_assistant_session','smart_assistant_message','smart_assistant_citation','smart_assistant_action_suggestion')",cancellationToken:ct));Check(assistantTables==4,"Persistência do Assistant","Tabelas de sessão, mensagem, citação e ação não estão disponíveis.");
  }
  catch(Exception exception){failures.Add("PostgreSQL");error.WriteLine($"[FALHA] PostgreSQL: {exception.GetType().Name}: {exception.Message}");}
  foreach(var warning in warnings)output.WriteLine($"[AVISO] {warning}");output.WriteLine($"RC24 Labels + Smart Assistant: {failures.Count} falha(s), {warnings.Count} aviso(s).");return failures.Count==0?0:2;
 }
 private static string Read(string root,params string[] path){var full=Path.Combine([root,..path]);return File.Exists(full)?File.ReadAllText(full):"";}
}
