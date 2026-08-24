using System.Text.Json;
using InovaGed.Application.EnvironmentDiagnostics;
using InovaGed.Environment.Doctor;
using InovaGed.Environment.Doctor.Checks;
using InovaGed.Environment.Doctor.Quality;
using Microsoft.Extensions.Configuration;
using Npgsql;
using BclEnvironment = global::System.Environment;

try
{
    var command = args.FirstOrDefault() ?? "check";
    if (command is "check" or "report")
    {
        var configuration = Configuration();
        var results = await new EnvironmentDoctor(configuration).CheckAsync();
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        var text = string.Join(BclEnvironment.NewLine, results.Select(r => $"[{r.Status}] {r.Category}/{r.Code}: {r.Title}{BclEnvironment.NewLine}  {r.Message}{(r.Recommendation is null ? "" : BclEnvironment.NewLine + "  Recomendação: " + r.Recommendation)}"));
        if (command == "report") { var output=Path.Combine(BclEnvironment.CurrentDirectory,"artifacts","environment-doctor");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"report.json"),json);await File.WriteAllTextAsync(Path.Combine(output,"report.txt"),text);Console.WriteLine($"Relatório seguro criado em {Path.GetRelativePath(BclEnvironment.CurrentDirectory,output)}"); } else Console.WriteLine(args.Contains("--json",StringComparer.Ordinal)?json:text);
        if(results.Any(r=>r.Blocking&&r.Status==EnvironmentCheckStatuses.Fail)) return 2;
        if(results.Any(r=>r.Status is EnvironmentCheckStatuses.Warning or EnvironmentCheckStatuses.NotVerifiable)) return 1;
        return 0;
    }
    var allowed = new[] { "quality-gate", "schema", "schema-check", "di", "di-check", "routes", "route-smoke", "razor", "razor-check", "icons", "icon-check", "dapper-mapping", "admin-links-check", "layout-check" };
    if (!allowed.Contains(command, StringComparer.OrdinalIgnoreCase)) { Console.Error.WriteLine("Uso: check [--json] | report | quality-gate|route-smoke|schema-check|di-check|razor-check|icon-check|dapper-mapping|admin-links-check|layout-check [--no-db-required]"); return 3; }
    var root=FindRoot(BclEnvironment.CurrentDirectory);var cfg=Configuration();var raw=cfg.GetConnectionString("DefaultConnection")??cfg.GetConnectionString("Postgres");var context=new QualityContext(root,args.Contains("--no-db-required",StringComparer.OrdinalIgnoreCase),raw,BclEnvironment.GetEnvironmentVariable("QUALITY_GATE_BASE_URL"));
    IQualityCheck[] all=[new MigrationFileQualityCheck(),new DependencyInjectionQualityCheck(),new PostgresSchemaQualityCheck(),new RazorQualityCheck(),new IconQualityCheck(),new RouteSmokeQualityCheck(),new AdministrationLinksQualityCheck(),new LayoutQualityCheck(),new DapperMappingQualityCheck()];
    var selected=command switch{"schema" or "schema-check"=>all.Where(x=>x is PostgresSchemaQualityCheck),"di" or "di-check"=>all.Where(x=>x is DependencyInjectionQualityCheck),"routes" or "route-smoke"=>all.Where(x=>x is RouteSmokeQualityCheck),"razor" or "razor-check"=>all.Where(x=>x is RazorQualityCheck),"icons" or "icon-check"=>all.Where(x=>x is IconQualityCheck),"dapper-mapping"=>all.Where(x=>x is DapperMappingQualityCheck),"admin-links-check"=>all.Where(x=>x is AdministrationLinksQualityCheck),"layout-check"=>all.Where(x=>x is LayoutQualityCheck),_=>all.AsEnumerable()};
    var report=new QualityGateReport{ConnectionString=Sanitize(raw)};report.Checks.Add(new("Build",QualityStatus.Pass,"Doctor iniciado a partir de assemblies compilados; use scripts/run-quality-gate.* para clean/restore/build."));foreach(var check in selected)try{report.Checks.AddRange(await check.RunAsync(context,CancellationToken.None));}catch(Exception e){report.Checks.Add(new(check.Name,QualityStatus.Fail,$"Check falhou com {e.GetType().Name}.","Validação incompleta.","Corrigir o próprio check; detalhes estão no console."));Console.Error.WriteLine($"{check.Name}: {e.Message}");}
    await QualityReportWriter.WriteAsync(root,report,CancellationToken.None);foreach(var x in report.Checks)Console.WriteLine($"[{x.Status.ToString().ToUpperInvariant()}] {x.Check}: {x.Message}");Console.WriteLine($"Status geral: {report.Status.ToString().ToUpperInvariant()} | Relatórios: artifacts/quality-gate");return report.Status==QualityStatus.Fail?2:report.Status==QualityStatus.Warning?1:0;
}
catch(JsonException exception){Console.Error.WriteLine($"Configuração inválida: {exception.GetType().Name}");return 3;}
catch(Exception exception){Console.Error.WriteLine($"Erro inesperado seguro: {exception.GetType().Name}");return 4;}

static IConfigurationRoot Configuration()=>new ConfigurationBuilder().SetBasePath(BclEnvironment.CurrentDirectory).AddJsonFile("InovaGed.Web/appsettings.json",optional:true).AddEnvironmentVariables().Build();
static string FindRoot(string start){for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"InovaGed.sln")))return d.FullName;throw new DirectoryNotFoundException("InovaGed.sln não encontrada.");}
static string Sanitize(string? value){if(string.IsNullOrWhiteSpace(value))return "Not configured";try{var b=new NpgsqlConnectionStringBuilder(value);if(!string.IsNullOrEmpty(b.Password))b.Password="***";return b.ConnectionString;}catch{return "Configured (invalid format; secret omitted)";}}
