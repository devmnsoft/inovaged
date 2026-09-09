using Dapper;
using Npgsql;

namespace InovaGed.Environment.Doctor.Checks;

public static class Rc25CanvasAssistantQualityCheck
{
 public static async Task<int> RunAsync(string root,string? connectionString,TextWriter output,TextWriter error,CancellationToken ct)
 {
  var failures=new List<string>();void Check(bool ok,string name,string detail){if(ok)output.WriteLine($"[OK] {name}");else{failures.Add(name);error.WriteLine($"[FALHA] {name}: {detail}");}}
  string Read(params string[] parts){var path=Path.Combine([root,..parts]);return File.Exists(path)?File.ReadAllText(path):"";}
  var catalog=Read("InovaGed.Infrastructure","PhysicalArchive","LabelTemplateCatalogService.cs");var resolver=Read("InovaGed.Infrastructure","Labels","LabelCanvasValueResolver.cs");var samples=Read("InovaGed.Infrastructure","Labels","LabelCanvasFieldCatalogService.cs");var designer=Read("InovaGed.Web","Controller","LabelDesignerController.cs");var wizard=Read("InovaGed.Web","Views","Labels","PrintWizard.cshtml");var branding=Read("InovaGed.Web","Controller","PrintBrandingController.cs");var labels=Read("InovaGed.Web","Controller","LabelsController.cs");var structured=Read("InovaGed.Infrastructure","SmartGed","Assistant","SmartAssistantStructuredRetrievalService.cs");var intent=Read("InovaGed.Infrastructure","SmartGed","Assistant","LocalSmartAssistantIntentResolver.cs");var service=Read("InovaGed.Infrastructure","SmartGed","Assistant","SmartGedAssistantService.cs");var di=Read("InovaGed.Infrastructure","DependencyInjection.cs");
  Check(new[]{"BOX\" or \"LOCDESKBOX","DOCUMENT\" or \"FOLDER\" or \"MEDICALRECORD\" or \"PROCESS\" or \"LOCDESKFOLDER","\"BATCH\" => \"BATCH\""}.All(catalog.Contains),"Tipos operacionais normalizados","Box/Folder/MedicalRecord/Process/Batch não compartilham o normalizador esperado.");
  Check(new[]{"boxCode","documentCode","folderName","recordNumber","patientName","processNumber","ocrStatus","retentionStatus"}.All(resolver.Contains),"Valores Canvas operacionais","O resolvedor não cobre todos os tipos semânticos.");
  var generic=samples.IndexOf("if (profile.Contains(\"Caixa\"",StringComparison.Ordinal);var genericText=generic>=0?samples[generic..]:"";Check(!new[]{"HOL.132.3","Hosp. Ophir Loyola","ARQUIVO LOCDESCK ANANINDEUA"}.Any(genericText.Contains),"Amostras genéricas neutras","Amostras não-HOL ainda contêm marca legada.");
  Check(designer.Contains("IPrintBrandingResolver")&&designer.Contains("/Labels/Designer/LivePreview")&&designer.Contains("ResolveDesignerPreviewValuesAsync"),"Preview real do Designer","Branding ou preview ao vivo não está integrado.");
  Check(wizard.Contains("asp-controller=\"LabelDesigner\"")&&wizard.Contains("summary-branding-client"),"PrintWizard integrado","Link do Designer ou resumo de branding está ausente.");
  Check(branding.Contains("GetCatalogAsync")&&branding.Contains("label_template_design"),"Bindings Canvas dinâmicos","Catálogo de bindings ainda é fixo.");
  Check(labels.Contains("_canvasValues.ResolveAsync")&&labels.Contains("printChannel=\"BATCH\"")&&labels.Contains("printedFields=values"),"Batch Canvas por origem","Lote não demonstra resolução e snapshot individual.");
  Check(structured.Contains("tenant_id=@TenantId")&&new[]{"LABEL_REPRINTS","LABEL_TEMPLATE","CLASSIFICATION_STATUS","RETENTION_STATUS","OCR_STATUS","QUALITY_STATUS","FIND_BOX","FIND_DOCUMENT"}.All(structured.Contains),"Retrieval estruturado isolado","Handlers obrigatórios ou tenant predicate ausentes.");
  Check(di.Contains("ISmartAssistantStructuredRetrievalService")&&intent.Contains("dateFrom")&&intent.Contains("\"COUNT\"")&&intent.Contains("BuildContext"),"Pipeline analítico contextual","Registro, datas relativas, COUNT ou contexto ausente.");
  Check(service.Contains("target_url")&&service.Contains("a.TargetType")&&service.Contains("SmartAssistantActionUrlPolicy")&&!service.Contains("TargetType=safeUrl"),"Action URL segura","URL ainda reutiliza target_type ou não é validada.");
  if(string.IsNullOrWhiteSpace(connectionString))output.WriteLine("[AVISO] Banco não configurado; validação PostgreSQL foi omitida.");else try{await using var db=new NpgsqlConnection(connectionString);await db.OpenAsync(ct);var column=await db.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from information_schema.columns where table_schema='ged' and table_name='smart_assistant_action_suggestion' and column_name='target_url')",cancellationToken:ct));Check(column,"Schema target_url aplicado","Migration RC25 pendente.");}catch(Exception ex){failures.Add("PostgreSQL");error.WriteLine($"[FALHA] PostgreSQL: {ex.GetType().Name}: {ex.Message}");}
  output.WriteLine($"RC25 Canvas + Smart Assistant: {failures.Count} falha(s).");return failures.Count==0?0:2;
 }
}
