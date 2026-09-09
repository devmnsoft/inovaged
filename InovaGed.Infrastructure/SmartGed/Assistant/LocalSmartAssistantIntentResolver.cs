using System.Globalization;
using System.Text.RegularExpressions;
using InovaGed.Application.SmartGed.Assistant;

namespace InovaGed.Infrastructure.SmartGed.Assistant;

public sealed class LocalSmartAssistantIntentResolver : ISmartAssistantIntentResolver
{
 public SmartAssistantIntentResult Resolve(string question,IReadOnlyList<SmartAssistantConversationMessage> history)
 {
  var q=Normalize(question);var context=BuildContext(history);var intent=ResolveIntent(q,context);var filters=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  Add(filters,"identifier",Regex.Match(question,@"\b(?:DOC|CX|PROC|PR|PROTOCOLO)?[-/. ]?\d{3,}\b",RegexOptions.IgnoreCase).Value);
  Add(filters,"boxCode",Regex.Match(question,@"\bCX[-/. ]?[A-Z0-9-]{2,}\b",RegexOptions.IgnoreCase).Value);
  Add(filters,"template",Regex.Match(question,@"\b[A-Z][A-Z0-9_]{4,}_V\d+\b",RegexOptions.IgnoreCase).Value);
  Add(filters,"client",Capture(question,@"(?:cliente|organiza(?:ção|cao))\s+([^,;?]+)"));Add(filters,"contract",Capture(question,@"contrato\s+([^,;?]+)"));
  if(ResolveDateRange(question,DateOnly.FromDateTime(DateTime.Now)) is{} range){filters["dateFrom"]=range.From.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);filters["dateTo"]=range.To.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
  var refers=Regex.IsMatch(q,@"\b(ele|ela|esse|essa|este|esta|nela|nele|nesse|nessa)\b");
  if(refers)
  {
   if(!filters.ContainsKey("template")&&!string.IsNullOrWhiteSpace(context.LastTemplateKey))filters["template"]=context.LastTemplateKey;
   if(!filters.ContainsKey("boxCode")&&!string.IsNullOrWhiteSpace(context.LastBoxCode))filters["boxCode"]=context.LastBoxCode;
   if(context.LastBoxId.HasValue)filters.TryAdd("boxId",context.LastBoxId.Value.ToString());if(context.LastDocumentId.HasValue)filters.TryAdd("documentId",context.LastDocumentId.Value.ToString());
   if(!string.IsNullOrWhiteSpace(context.LastClientName))filters.TryAdd("client",context.LastClientName);if(!string.IsNullOrWhiteSpace(context.LastContractName))filters.TryAdd("contract",context.LastContractName);
  }
  var aggregation=Regex.IsMatch(q,@"\b(quantos|quantas|total|quantidade)\b")?"COUNT":null;
  return new(intent,filters,IntentConfidence(intent),aggregation,context);
 }

 public static (DateOnly From,DateOnly To)? ResolveDateRange(string question,DateOnly today)
 {
  var q=Normalize(question);var between=Regex.Match(q,@"entre\s+(\d{2}/\d{2}/\d{4})\s+e\s+(\d{2}/\d{2}/\d{4})");
  if(between.Success&&DateOnly.TryParseExact(between.Groups[1].Value,"dd/MM/yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var from)&&DateOnly.TryParseExact(between.Groups[2].Value,"dd/MM/yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var through))return(from,through.AddDays(1));
  var year=Regex.Match(q,@"\bem\s+(20\d{2})\b");if(year.Success){var y=int.Parse(year.Groups[1].Value,CultureInfo.InvariantCulture);return(new(y,1,1),new(y+1,1,1));}
  if(q.Contains("ontem"))return(today.AddDays(-1),today);if(q.Contains("hoje"))return(today,today.AddDays(1));
  var monday=today.AddDays(-(((int)today.DayOfWeek+6)%7));if(q.Contains("semana passada"))return(monday.AddDays(-7),monday);if(q.Contains("esta semana"))return(monday,monday.AddDays(7));
  var month=new DateOnly(today.Year,today.Month,1);if(q.Contains("mes passado")||q.Contains("mês passado"))return(month.AddMonths(-1),month);if(q.Contains("este mes")||q.Contains("este mês"))return(month,month.AddMonths(1));
  return null;
 }

 public static SmartAssistantConversationContext BuildContext(IReadOnlyList<SmartAssistantConversationMessage> history)
 {
  Guid? documentId=null,boxId=null,brandingId=null;string? documentCode=null,boxCode=null,template=null,client=null,contract=null;
  foreach(var content in history.Reverse().Select(x=>x.Content))
  {
   template??=Match(content,@"\b[A-Z][A-Z0-9_]{4,}_V\d+\b");boxCode??=Match(content,@"\bCX[-/. ]?[A-Z0-9-]{2,}\b");documentCode??=Match(content,@"\b(?:DOC|PROC|PR)[-/. ]?[A-Z0-9-]{2,}\b");
   boxId??=GuidFrom(content,@"(?:Boxes|boxId=)/?([0-9a-f-]{36})");documentId??=GuidFrom(content,@"(?:Documents/Details|documentId=)/?([0-9a-f-]{36})");brandingId??=GuidFrom(content,@"PrintBranding/Profiles/([0-9a-f-]{36})");
   client??=Capture(content,@"Cliente:\s*([^\r\n.]+)");contract??=Capture(content,@"Contrato:\s*([^\r\n.]+)");
  }
  return new(documentId,documentCode,boxId,boxCode,template,brandingId,client,contract);
 }

 private static string ResolveIntent(string q,SmartAssistantConversationContext context)
 {
  if(Has(q,"reimpress","impresso quant","impressões","impressoes"))return "LABEL_REPRINTS";
  if(Has(q,"template","modelo de etiqueta")&&Has(q,"rascunho","publicad","sem perfil"))return "LABEL_TEMPLATE";
  if(Has(q,"branding","identidade visual","perfil visual","logo"))return q.Contains("cliente")?"CLIENT_PROFILE":"LABEL_BRANDING";
  if(Has(q,"template","modelo de etiqueta"))return "LABEL_TEMPLATE";if(Has(q,"canvas","designer de etiqueta","layout da etiqueta"))return "LABEL_DESIGN";if(Has(q,"etiqueta","rotulo","rótulo"))return "LABEL_STATUS";
  if(Has(q,"documento","documentos")&&Has(q,"caixa","nela","nessa")&&(!string.IsNullOrWhiteSpace(context.LastBoxCode)||context.LastBoxId.HasValue))return "FIND_DOCUMENT";
  if(Has(q,"caixa","box","localiza"))return "FIND_BOX";if(Has(q,"ocr","texto extraído","texto extraido"))return "OCR_STATUS";if(Has(q,"classifica"))return "CLASSIFICATION_STATUS";if(Has(q,"temporal","retenção","retencao","elimina","destinação","destinacao"))return "RETENTION_STATUS";if(Has(q,"qualidade","inconsist","pendência","pendencia"))return "QUALITY_STATUS";if(Has(q,"resuma","resumo","sintetize"))return "DOCUMENT_SUMMARY";if(Has(q,"documento","processo","protocolo"))return "FIND_DOCUMENT";return "GENERAL_GED_SEARCH";
 }
 private static bool Has(string q,params string[] terms)=>terms.Any(q.Contains);private static decimal IntentConfidence(string intent)=>intent=="GENERAL_GED_SEARCH"?62:92;private static string Normalize(string value)=>value.Trim().ToLowerInvariant();
 private static string Capture(string value,string pattern){var m=Regex.Match(value,pattern,RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value.Trim():"";}private static string? Match(string value,string pattern){var m=Regex.Match(value,pattern,RegexOptions.IgnoreCase);return m.Success?m.Value:null;}
 private static Guid? GuidFrom(string value,string pattern){var m=Regex.Match(value,pattern,RegexOptions.IgnoreCase);return m.Success&&Guid.TryParse(m.Groups[1].Value,out var id)?id:null;}private static void Add(IDictionary<string,string> target,string key,string value){if(!string.IsNullOrWhiteSpace(value)&&!value.StartsWith("esse",StringComparison.OrdinalIgnoreCase)&&!value.StartsWith("essa",StringComparison.OrdinalIgnoreCase))target[key]=value.Trim();}
}
