using System.Text;
using InovaGed.Application.SmartGed.Assistant;

namespace InovaGed.Infrastructure.SmartGed.Assistant;

public sealed class LocalSmartAssistantAnswerComposer : ISmartAssistantAnswerComposer
{
 public Task<SmartAssistantAnswerDraft> ComposeAsync(SmartAssistantAnswerInput input,CancellationToken ct)
 {
  var evidence=input.Retrieval.Evidence;var structured=input.Structured??input.Retrieval.Structured;
  if(input.Intent?.Aggregation=="COUNT"&&structured?.TotalCount is int total)
  {
   var label=input.Retrieval.ResolvedIntent switch{"LABEL_REPRINTS"=>"reimpressões","CLASSIFICATION_STATUS"=>"documentos sem classificação","OCR_STATUS"=>"documentos com OCR pendente","LABEL_STATUS"=>"impressões",_=>"registros"};
   var countAnswer=new StringBuilder($"Foram encontrados {total} {label} no período solicitado.");foreach(var metric in structured.Metrics.Where(x=>x.Value>0))countAnswer.AppendLine().Append("• ").Append(metric.Value).Append(' ').Append(metric.Key.ToLowerInvariant());
   if(structured.SummaryFacts.TryGetValue("topTemplate",out var top)&&!string.IsNullOrWhiteSpace(top))countAnswer.AppendLine().AppendLine().Append("O template mais utilizado foi \"").Append(top).Append("\".");
   var countActions=evidence.Count>0?Actions(input.Retrieval.ResolvedIntent,evidence[0]):DefaultActions(input.Retrieval.ResolvedIntent);return Task.FromResult(new SmartAssistantAnswerDraft(countAnswer.ToString(),92,"ANSWERED",countActions,input.Retrieval.Warnings));
  }
  if(structured?.Rows.FirstOrDefault() is{} row&&(input.Retrieval.ResolvedIntent is "LABEL_TEMPLATE" or "LABEL_DESIGN" or "LABEL_BRANDING" or "CLIENT_PROFILE"))
  {
   var f=row.Facts;var templateAnswer=new StringBuilder();if(f.TryGetValue("template",out var template)&&!string.IsNullOrWhiteSpace(template))templateAnswer.Append("O template \"").Append(row.Title).Append("\" (").Append(template).Append(')');else templateAnswer.Append("O perfil visual \"").Append(row.Title).Append('"');
   if(f.TryGetValue("status",out var status)&&!string.IsNullOrWhiteSpace(status))templateAnswer.Append(" está ").Append(status.Equals("PUBLISHED",StringComparison.OrdinalIgnoreCase)?"publicado":"em rascunho");if(f.TryGetValue("version",out var version)&&!string.IsNullOrWhiteSpace(version))templateAnswer.Append(" na versão ").Append(version);templateAnswer.AppendLine(".");
   AddFact(templateAnswer,"Perfil visual",f,"profile");AddFact(templateAnswer,"Cliente",f,"client");AddFact(templateAnswer,"Contrato",f,"contract");AddFact(templateAnswer,"Organização",f,"organization");AddFact(templateAnswer,"Logo",f,"logo");
   return Task.FromResult(new SmartAssistantAnswerDraft(templateAnswer.ToString().Trim(),90,"ANSWERED",evidence.Count>0?Actions(input.Retrieval.ResolvedIntent,evidence[0]):[],input.Retrieval.Warnings));
  }
  if(evidence.Count==0)return Task.FromResult(new SmartAssistantAnswerDraft("Não encontrei evidência suficiente no acervo deste cliente para responder com segurança. Informe um código, processo, protocolo, caixa, cliente, contrato, template ou período.",0,"INSUFFICIENT_EVIDENCE",[],input.Retrieval.Warnings));
  var confidence=Math.Round(evidence.Take(5).Average(x=>x.Confidence),1);var band=confidence>=85?"alta":confidence>=70?"média":confidence>=55?"baixa":"insuficiente";
  var title=Heading(input.Retrieval.ResolvedIntent,evidence.Count);var answer=new StringBuilder(title).AppendLine().AppendLine();
  foreach(var item in evidence.Take(5))answer.Append("- ").Append(item.Title).Append(": ").Append(Trim(item.Excerpt,280)).AppendLine();
  answer.AppendLine().Append("Confiança ").Append(band).Append(" (").Append(confidence.ToString("0.#")).Append("%). A resposta usa somente os registros citados abaixo.");
  var actions=Actions(input.Retrieval.ResolvedIntent,evidence.First());
  return Task.FromResult(new SmartAssistantAnswerDraft(answer.ToString(),confidence,confidence>=55?"ANSWERED":"INSUFFICIENT_EVIDENCE",actions,input.Retrieval.Warnings));
 }
 private static string Heading(string intent,int count)=>intent switch
 {
  "FIND_DOCUMENT"=>$"Localizei {count} evidência(s) documental(is) relevante(s).",
  "FIND_BOX"=>$"Localizei {count} evidência(s) sobre caixas e localização física.",
  "DOCUMENT_SUMMARY"=>"Síntese documental baseada nas evidências encontradas:",
  "LABEL_REPRINTS"=>$"Localizei {count} registro(s) de impressão ou reimpressão.",
  "LABEL_TEMPLATE" or "LABEL_DESIGN"=>"Situação dos templates e designs de etiqueta:",
  "LABEL_BRANDING" or "CLIENT_PROFILE"=>"Identidade visual e associações de etiqueta encontradas:",
  "OCR_STATUS"=>"Situação de OCR encontrada:","CLASSIFICATION_STATUS"=>"Situação de classificação encontrada:","RETENTION_STATUS"=>"Situação de temporalidade encontrada:","QUALITY_STATUS"=>"Pendências de qualidade encontradas:",
  _=>$"Encontrei {count} evidência(s) no acervo GED."
 };
 private static IReadOnlyList<SmartAssistantActionDraft> Actions(string intent,SmartAssistantEvidence first)
 {
  var (type,title,target,url)=intent switch
  {
   "FIND_BOX"=>("OPEN_BOX","Abrir caixa","BOX",first.Url),
   "LABEL_DESIGN"=>("OPEN_LABEL_DESIGNER","Abrir designer de etiquetas","LABEL_TEMPLATE",first.Url??"/Labels/Designer"),
   "LABEL_TEMPLATE"=>("OPEN_LABEL_TEMPLATE","Abrir template de etiqueta","LABEL_TEMPLATE",first.Url??"/Labels/Designer"),
   "LABEL_REPRINTS" or "LABEL_STATUS"=>("OPEN_LABEL_HISTORY","Abrir histórico de etiquetas","LABEL_TEMPLATE","/Labels/History"),
   "LABEL_BRANDING" or "CLIENT_PROFILE"=>("OPEN_BRANDING_PROFILE","Abrir identidade visual","BRANDING_PROFILE",first.Url??"/Administration/PrintBranding"),
   "CLASSIFICATION_STATUS"=>("OPEN_CLASSIFICATION_REVIEW","Abrir revisão de classificação","DOCUMENT",first.Url??"/Ged/Search?classification=missing"),
   "RETENTION_STATUS"=>("OPEN_RETENTION_REVIEW","Abrir revisão de temporalidade","DOCUMENT",first.Url??"/Retention"),
   "OCR_STATUS"=>("OPEN_OCR_REVIEW","Abrir revisão de OCR","DOCUMENT",first.Url??"/Ocr"),
   _=>("OPEN_DOCUMENT","Abrir documento","DOCUMENT",first.Url)
  };
  if(string.IsNullOrWhiteSpace(url))return [];
  var actions=new List<SmartAssistantActionDraft>{new(type,title,"Abertura segura para revisão humana; nenhuma alteração será executada.",target,first.SourceId,url)};
  if(intent=="FIND_BOX"&&first.SourceId is Guid boxId)actions.Add(new("OPEN_PRINT_WIZARD","Preparar etiqueta da caixa","Apenas preenche o assistente de impressão; nenhuma impressão será executada.","BOX",boxId,$"/Labels/PrintWizard?subjectType=BOX&subjectId={boxId}"));
  return actions;
 }
 private static IReadOnlyList<SmartAssistantActionDraft> DefaultActions(string intent)=>intent switch{"LABEL_REPRINTS" or "LABEL_STATUS"=>[new("OPEN_LABEL_HISTORY","Abrir histórico de etiquetas","Abertura segura para revisão humana.","LABEL_TEMPLATE",null,"/Labels/History")],"CLASSIFICATION_STATUS"=>[new("OPEN_CLASSIFICATION_REVIEW","Abrir revisão de classificação","Abertura segura para revisão humana.","DOCUMENT",null,"/Ged/Search?classification=missing")],"OCR_STATUS"=>[new("OPEN_OCR_REVIEW","Abrir revisão de OCR","Abertura segura para revisão humana.","DOCUMENT",null,"/Ocr")],_=>[]};
 private static void AddFact(StringBuilder answer,string label,IReadOnlyDictionary<string,string> facts,string key){if(facts.TryGetValue(key,out var value)&&!string.IsNullOrWhiteSpace(value))answer.Append(label).Append(": ").Append(value).AppendLine(".");}
 private static string Trim(string value,int max)=>value.Length<=max?value:value[..max]+"…";
}
