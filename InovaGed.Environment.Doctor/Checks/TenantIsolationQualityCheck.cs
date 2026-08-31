using System.Text.RegularExpressions;
using InovaGed.Environment.Doctor.Quality;
namespace InovaGed.Environment.Doctor.Checks;
public sealed class TenantIsolationQualityCheck:IQualityCheck
{
 public string Name=>"Tenant Isolation";
 private static readonly string[] Modules=["Documents","Labels","Physical","Classification","SmartGed","SmartAssistant","SmartWorkflow","Governance","ContractMeasurement","FiscalPortal"];
 public async Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext c,CancellationToken ct){var issues=new List<QualityFinding>();var infra=Path.Combine(c.Root,"InovaGed.Infrastructure");foreach(var file in Directory.EnumerateFiles(infra,"*.cs",SearchOption.AllDirectories).Where(x=>Modules.Any(m=>x.Contains(m,StringComparison.OrdinalIgnoreCase)))){var text=await File.ReadAllTextAsync(file,ct);foreach(Match q in Regex.Matches(text,@"(?is)\b(select|update|delete)\b.{0,1200}?\bfrom\s+(?:ged\.)?\w+|\bupdate\s+(?:ged\.)?\w+.{0,1200}")){if(q.Value.Contains("tenant_id",StringComparison.OrdinalIgnoreCase))continue;var rel=Path.GetRelativePath(c.Root,file);issues.Add(new(Name,QualityStatus.Warning,$"Query operacional sem tenant_id visível: {rel}","Pode haver acesso transversal se o repositório não aplicar escopo externo.","Confirmar filtro tenant_id ou documentar isolamento estrutural.",Resource:rel));break;}}return issues.Count==0?[new(Name,QualityStatus.Pass,"Queries operacionais analisadas contêm isolamento explícito por tenant.")]:issues;}
}
