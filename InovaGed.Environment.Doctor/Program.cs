using System.Text.Json;
using InovaGed.Application.EnvironmentDiagnostics;
using InovaGed.Environment.Doctor;
using InovaGed.Environment.Doctor.Checks;
using InovaGed.Environment.Doctor.Quality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using InovaGed.Application.SystemHealth.Migrations;
using InovaGed.Infrastructure;
using Npgsql;
using BclEnvironment = global::System.Environment;

try
{
    var command = args.FirstOrDefault() ?? "check";
    if (command.Equals("labels-visual-quality", StringComparison.OrdinalIgnoreCase))
    {
        var repositoryRoot = FindRoot(BclEnvironment.CurrentDirectory);
        return LabelsVisualQualityCheck.Run(repositoryRoot, Console.Out, Console.Error);
    }
    if (command.Equals("labels-logo-rendering", StringComparison.OrdinalIgnoreCase))
    {
        var repositoryRoot = FindRoot(BclEnvironment.CurrentDirectory);
        return LabelsLogoRenderingCheck.Run(repositoryRoot, Console.Out, Console.Error);
    }
    if (command is "database-readiness" or "apply-required-migrations")
    {
        var repositoryRoot = FindRoot(BclEnvironment.CurrentDirectory);
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Configuration.AddConfiguration(Configuration());
        hostBuilder.Services.AddDatabaseModule(hostBuilder.Configuration);
        using var host = hostBuilder.Build();
        using var scope = host.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IDatabaseMigrationRunner>();
        if (command == "database-readiness")
        {
            var plan = await runner.GetPlanAsync(CancellationToken.None);
            Console.WriteLine($"Migrations obrigatórias: {plan.Total} | Aplicadas: {plan.Applied} | Pendentes: {plan.Pending} | Falhas: {plan.Failed}");
            foreach (var item in plan.Items) Console.WriteLine($"[{(item.Applied ? "APLICADA" : "PENDENTE")}] {item.Area}/{item.Name} SHA-256={item.ChecksumSha256 ?? "indisponível"}{(item.LastError is null ? "" : " | " + item.LastError)}");
            return plan.Pending == 0 ? 0 : 1;
        }
        var result = await runner.ApplyRequiredAsync(null, "InovaGed.Environment.Doctor", CancellationToken.None);
        foreach (var item in result.Items) Console.WriteLine($"[{(item.Success ? "OK" : "FALHA")}] {item.Name} ({item.DurationMs} ms){(item.ErrorMessage is null ? "" : " | " + item.ErrorMessage)}");
        Console.WriteLine(result.Message);
        return result.Success ? 0 : 2;
    }
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
    var allowed = new[] { "quality-gate", "schema", "schema-check", "di", "di-check", "routes", "route-smoke", "razor", "razor-check", "razor-safety", "icons", "icon-check", "dapper-mapping", "dapper-safety", "migration-consistency", "security-scan", "tenant-isolation", "performance-check", "admin-links-check", "layout-check", "incidents", "uat-readiness", "ui-consistency" };
    if (!allowed.Contains(command, StringComparer.OrdinalIgnoreCase)) { Console.Error.WriteLine("Uso: check [--json] | report | quality-gate|route-smoke|schema-check|di-check|razor-check|icon-check|dapper-mapping|admin-links-check|layout-check|ui-consistency [--no-db-required]"); return 3; }
    var root=FindRoot(BclEnvironment.CurrentDirectory);var cfg=Configuration();var raw=cfg.GetConnectionString("DefaultConnection")??cfg.GetConnectionString("Postgres");var context=new QualityContext(root,args.Contains("--no-db-required",StringComparer.OrdinalIgnoreCase),raw,BclEnvironment.GetEnvironmentVariable("QUALITY_GATE_BASE_URL"));
    IQualityCheck[] all=[new MigrationFileQualityCheck(),new DependencyInjectionQualityCheck(),new PostgresSchemaQualityCheck(),new RazorQualityCheck(),new IconQualityCheck(),new RouteSmokeQualityCheck(),new AdministrationLinksQualityCheck(),new LayoutQualityCheck(),new UiConsistencyQualityCheck(),new DapperMappingQualityCheck(),new SecurityQualityCheck(),new TenantIsolationQualityCheck(),new PerformanceQualityCheck(),new IncidentCenterQualityCheck(),new UatReadinessQualityCheck()];
    var selected=command switch{"schema" or "schema-check"=>all.Where(x=>x is PostgresSchemaQualityCheck),"di" or "di-check"=>all.Where(x=>x is DependencyInjectionQualityCheck),"routes" or "route-smoke"=>all.Where(x=>x is RouteSmokeQualityCheck),"razor" or "razor-check" or "razor-safety"=>all.Where(x=>x is RazorQualityCheck),"icons" or "icon-check"=>all.Where(x=>x is IconQualityCheck),"dapper-mapping" or "dapper-safety"=>all.Where(x=>x is DapperMappingQualityCheck),"migration-consistency"=>all.Where(x=>x is MigrationFileQualityCheck),"security-scan"=>all.Where(x=>x is SecurityQualityCheck),"tenant-isolation"=>all.Where(x=>x is TenantIsolationQualityCheck),"performance-check"=>all.Where(x=>x is PerformanceQualityCheck),"admin-links-check"=>all.Where(x=>x is AdministrationLinksQualityCheck),"layout-check"=>all.Where(x=>x is LayoutQualityCheck),"incidents"=>all.Where(x=>x is IncidentCenterQualityCheck),"uat-readiness"=>all.Where(x=>x is UatReadinessQualityCheck),"ui-consistency"=>all.Where(x=>x is UiConsistencyQualityCheck),_=>all.AsEnumerable()};
    var report=new QualityGateReport{ConnectionString=Sanitize(raw)};report.Checks.Add(new("Build",QualityStatus.Pass,"Doctor iniciado a partir de assemblies compilados; use scripts/run-quality-gate.* para clean/restore/build."));foreach(var check in selected)try{report.Checks.AddRange(await check.RunAsync(context,CancellationToken.None));}catch(Exception e){report.Checks.Add(new(check.Name,QualityStatus.Fail,$"Check falhou com {e.GetType().Name}.","Validação incompleta.","Corrigir o próprio check; detalhes estão no console."));Console.Error.WriteLine($"{check.Name}: {e.Message}");}
    await QualityReportWriter.WriteAsync(root,report,CancellationToken.None);foreach(var x in report.Checks)Console.WriteLine($"[{x.Status.ToString().ToUpperInvariant()}] {x.Check}: {x.Message}");Console.WriteLine($"Status geral: {report.Status.ToString().ToUpperInvariant()} | Relatórios: artifacts/quality-gate");return report.Status==QualityStatus.Fail?2:report.Status==QualityStatus.Warning?1:0;
}
catch(JsonException exception){Console.Error.WriteLine($"Configuração inválida: {exception.GetType().Name}");return 3;}
catch(Exception exception){Console.Error.WriteLine($"Erro inesperado seguro: {exception.GetType().Name}");return 4;}

static IConfigurationRoot Configuration()=>new ConfigurationBuilder().SetBasePath(BclEnvironment.CurrentDirectory).AddJsonFile("InovaGed.Web/appsettings.json",optional:true).AddEnvironmentVariables().Build();
static string FindRoot(string start){for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"InovaGed.sln")))return d.FullName;throw new DirectoryNotFoundException("InovaGed.sln não encontrada.");}
static string Sanitize(string? value){if(string.IsNullOrWhiteSpace(value))return "Not configured";try{var b=new NpgsqlConnectionStringBuilder(value);if(!string.IsNullOrEmpty(b.Password))b.Password="***";return b.ConnectionString;}catch{return "Configured (invalid format; secret omitted)";}}
