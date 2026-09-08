using System.Text;
using InovaGed.Application.SmartGed.Assistant;

namespace InovaGed.Infrastructure.SmartGed.Assistant;

public sealed class LocalSmartAssistantAnswerComposer : ISmartAssistantAnswerComposer
{
 public Task<SmartAssistantAnswerDraft> ComposeAsync(SmartAssistantAnswerInput input,CancellationToken ct)
 {
  var evidence=input.Retrieval.Evidence;
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
  var (type,title,url)=intent switch
  {
   "FIND_BOX"=>("OPEN_BOX","Abrir caixa",first.Url),
   "LABEL_DESIGN"=>("OPEN_LABEL_DESIGNER","Abrir designer de etiquetas",first.Url??"/Labels/Designer"),
   "LABEL_TEMPLATE"=>("OPEN_LABEL_TEMPLATE","Abrir template de etiqueta",first.Url??"/Labels/Designer"),
   "LABEL_REPRINTS" or "LABEL_STATUS"=>("OPEN_LABEL_HISTORY","Abrir histórico de etiquetas","/Labels/History"),
   "LABEL_BRANDING" or "CLIENT_PROFILE"=>("OPEN_BRANDING_PROFILE","Abrir identidade visual",first.Url??"/Administration/PrintBranding"),
   "CLASSIFICATION_STATUS"=>("OPEN_CLASSIFICATION_REVIEW","Abrir revisão de classificação",first.Url),
   "RETENTION_STATUS"=>("OPEN_RETENTION_REVIEW","Abrir revisão de temporalidade",first.Url),
   "OCR_STATUS"=>("OPEN_OCR_REVIEW","Abrir revisão de OCR",first.Url),
   _=>("OPEN_DOCUMENT","Abrir documento",first.Url)
  };
  return string.IsNullOrWhiteSpace(url)?[]:[new SmartAssistantActionDraft(type,title,"Abertura segura para revisão humana; nenhuma alteração será executada.",url,null,url)];
 }
 private static string Trim(string value,int max)=>value.Length<=max?value:value[..max]+"…";
}
