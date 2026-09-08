using System.Text.RegularExpressions;
using InovaGed.Application.SmartGed.Assistant;

namespace InovaGed.Infrastructure.SmartGed.Assistant;

public sealed class LocalSmartAssistantIntentResolver : ISmartAssistantIntentResolver
{
 public SmartAssistantIntentResult Resolve(string question,IReadOnlyList<SmartAssistantConversationMessage> history)
 {
  var q=Normalize(question);var intent=ResolveIntent(q);var filters=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  Add(filters,"identifier",Regex.Match(question,@"\b(?:DOC|CX|PROC|PR|PROTOCOLO)?[-/. ]?\d{3,}\b",RegexOptions.IgnoreCase).Value);
  Add(filters,"template",Regex.Match(question,@"\b[A-Z][A-Z0-9_]{4,}_V\d+\b",RegexOptions.IgnoreCase).Value);
  Add(filters,"date",Regex.Match(question,@"\b\d{2}/\d{2}/\d{4}\b").Value);
  Add(filters,"client",Capture(question,@"(?:cliente|organiza(?:ção|cao))\s+([^,;?]+)"));
  Add(filters,"contract",Capture(question,@"contrato\s+([^,;?]+)"));
  return new(intent,filters,IntentConfidence(intent));
 }
 private static string ResolveIntent(string q)
 {
  if(Has(q,"reimpress","impresso quant","impressões","impressoes"))return "LABEL_REPRINTS";
  if(Has(q,"branding","identidade visual","perfil visual","logo"))return q.Contains("cliente")?"CLIENT_PROFILE":"LABEL_BRANDING";
  if(Has(q,"template","modelo de etiqueta"))return "LABEL_TEMPLATE";
  if(Has(q,"canvas","designer de etiqueta","layout da etiqueta"))return "LABEL_DESIGN";
  if(Has(q,"etiqueta","rotulo","rótulo"))return "LABEL_STATUS";
  if(Has(q,"caixa","box","localiza"))return "FIND_BOX";
  if(Has(q,"ocr","texto extraído","texto extraido"))return "OCR_STATUS";
  if(Has(q,"classifica"))return "CLASSIFICATION_STATUS";
  if(Has(q,"temporal","retenção","retencao","elimina","destinação","destinacao"))return "RETENTION_STATUS";
  if(Has(q,"qualidade","inconsist","pendência","pendencia"))return "QUALITY_STATUS";
  if(Has(q,"resuma","resumo","sintetize"))return "DOCUMENT_SUMMARY";
  if(Has(q,"documento","processo","protocolo"))return "FIND_DOCUMENT";
  return "GENERAL_GED_SEARCH";
 }
 private static bool Has(string q,params string[] terms)=>terms.Any(q.Contains);
 private static decimal IntentConfidence(string intent)=>intent=="GENERAL_GED_SEARCH"?62:92;
 private static string Normalize(string value)=>value.Trim().ToLowerInvariant();
 private static string Capture(string value,string pattern){var m=Regex.Match(value,pattern,RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value.Trim():"";}
 private static void Add(IDictionary<string,string> target,string key,string value){if(!string.IsNullOrWhiteSpace(value))target[key]=value.Trim();}
}
