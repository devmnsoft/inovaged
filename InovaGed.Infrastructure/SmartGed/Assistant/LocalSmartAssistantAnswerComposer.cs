using InovaGed.Application.SmartGed.Assistant;
namespace InovaGed.Infrastructure.SmartGed.Assistant;
public sealed class LocalSmartAssistantAnswerComposer : ISmartAssistantAnswerComposer
{
 public Task<SmartAssistantAnswerDraft> ComposeAsync(SmartAssistantAnswerInput input,CancellationToken ct)
 {
  if(input.Retrieval.Evidence.Count==0)return Task.FromResult(new SmartAssistantAnswerDraft("Não encontrei evidência suficiente no acervo para responder com segurança. Tente informar número de processo, assunto, caixa, protocolo ou período.",0,"INSUFFICIENT_EVIDENCE",[],input.Retrieval.Warnings));
  var groups=input.Retrieval.Evidence.GroupBy(x=>x.SourceType).Select(x=>$"{x.Count()} fonte(s) {x.Key}");
  var text=$"Encontrei {input.Retrieval.Evidence.Count} evidência(s) relacionada(s) à pergunta: {string.Join(", ",groups)}. Consulte as fontes citadas antes de tomar uma decisão arquivística.";
  var first=input.Retrieval.Evidence.First();
  var action=new SmartAssistantActionDraft(ActionType(input.Question),ActionTitle(input.Question),"Recomendação para revisão humana; nenhuma alteração foi executada.",first.SourceType,first.SourceId);
  return Task.FromResult(new SmartAssistantAnswerDraft(text,input.Retrieval.Evidence.Average(x=>x.Confidence),"ANSWERED",[action],input.Retrieval.Warnings));
 }
 private static string ActionType(string q)=>q.Contains("ocr",StringComparison.OrdinalIgnoreCase)?"ANALYZE_OCR":q.Contains("classifica",StringComparison.OrdinalIgnoreCase)?"REVIEW_CLASSIFICATION":q.Contains("temporal",StringComparison.OrdinalIgnoreCase)||q.Contains("elimina",StringComparison.OrdinalIgnoreCase)?"REVIEW_RETENTION":"OPEN_REVIEW_QUEUE";
 private static string ActionTitle(string q)=>ActionType(q) switch{"ANALYZE_OCR"=>"Analisar documento com OCR/inteligência","REVIEW_CLASSIFICATION"=>"Revisar classificação","REVIEW_RETENTION"=>"Revisar temporalidade",_=>"Abrir fila de revisão"};
}
