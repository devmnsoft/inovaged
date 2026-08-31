using System.Text.RegularExpressions;
using InovaGed.Environment.Doctor.Quality;
namespace InovaGed.Environment.Doctor.Checks;
public sealed class PerformanceQualityCheck:IQualityCheck
{
 public string Name=>"Performance";
 public async Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext c,CancellationToken ct){var issues=new List<QualityFinding>();foreach(var file in Directory.EnumerateFiles(Path.Combine(c.Root,"InovaGed.Infrastructure"),"*.cs",SearchOption.AllDirectories)){var text=await File.ReadAllTextAsync(file,ct);if(Regex.IsMatch(text,@"(?is)select\s+\*\s+from"))issues.Add(Warn(c,file,"SELECT * detectado; selecionar somente colunas necessárias."));foreach(Match m in Regex.Matches(text,@"(?is)select\b.{0,2600}?\border\s+by\b.{0,500}"))if(m.Value.Contains("order by",StringComparison.OrdinalIgnoreCase)&&!m.Value.Contains(" limit ",StringComparison.OrdinalIgnoreCase)&&!m.Value.Contains(" offset ",StringComparison.OrdinalIgnoreCase)) {issues.Add(Warn(c,file,"Listagem ordenada sem LIMIT/OFFSET visível."));break;}}return issues.Count==0?[new(Name,QualityStatus.Pass,"Nenhum padrão crítico de consulta ilimitada foi detectado.")]:issues;}
 private QualityFinding Warn(QualityContext c,string f,string m)=>new(Name,QualityStatus.Warning,$"{m} {Path.GetRelativePath(c.Root,f)}","Carga excessiva e latência.","Adicionar paginação: page >= 1, pageSize entre 1 e 100.",Resource:Path.GetRelativePath(c.Root,f));
}
