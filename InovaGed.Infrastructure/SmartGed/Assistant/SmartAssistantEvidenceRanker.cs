using System.Text.RegularExpressions;
using InovaGed.Application.SmartGed.Assistant;

namespace InovaGed.Infrastructure.SmartGed.Assistant;

public sealed class SmartAssistantEvidenceRanker : ISmartAssistantEvidenceRanker
{
 public IReadOnlyList<SmartAssistantEvidence> Rank(string question,IEnumerable<SmartAssistantEvidence> evidence,int limit)
 {
  var terms=Regex.Matches(question.ToLowerInvariant(),@"[\p{L}\p{N}_./-]{3,}").Select(x=>x.Value).Distinct().Take(10).ToArray();
  return evidence.Select(e=>Score(e,terms)).OrderByDescending(x=>x.Confidence).ThenBy(x=>x.Title).Take(Math.Clamp(limit,1,8)).ToArray();
 }
 private static SmartAssistantEvidence Score(SmartAssistantEvidence e,string[] terms)
 {
  var haystack=$"{e.Title} {e.Excerpt}".ToLowerInvariant();var matches=terms.Count(haystack.Contains);var exact=terms.Any(t=>e.Title.Contains(t,StringComparison.OrdinalIgnoreCase));
  var score=Math.Clamp((exact?88m:matches==terms.Length&&terms.Length>0?78m:matches>0?62m:45m)+(e.SourceId.HasValue?3:0),0,97);
  var reason=exact?"identificador ou título exato":matches>1?"múltiplos termos coincidentes":matches==1?"termo relacionado":"evidência contextual";
  return e with{Confidence=Math.Max(e.Confidence,score),RankReason=reason};
 }
}
