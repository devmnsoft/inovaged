using System.Text.Json;
using InovaGed.Application.EnvironmentDiagnostics;
using InovaGed.Environment.Doctor;
using Microsoft.Extensions.Configuration;

try
{
    var command = args.FirstOrDefault() ?? "check";
    if (command is not ("check" or "report"))
    {
        Console.Error.WriteLine("Uso: check [--json] | report");
        return 3;
    }
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Environment.CurrentDirectory)
        .AddJsonFile("InovaGed.Web/appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    var results = await new EnvironmentDoctor(configuration).CheckAsync();
    var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    var text = string.Join(Environment.NewLine, results.Select(r => $"[{r.Status}] {r.Category}/{r.Code}: {r.Title}{Environment.NewLine}  {r.Message}{(r.Recommendation is null ? "" : Environment.NewLine + "  Recomendação: " + r.Recommendation)}"));
    if (command == "report")
    {
        var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "environment-doctor");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), json);
        await File.WriteAllTextAsync(Path.Combine(output, "report.txt"), text);
        Console.WriteLine($"Relatório seguro criado em {Path.GetRelativePath(Environment.CurrentDirectory, output)}");
    }
    else Console.WriteLine(args.Contains("--json", StringComparer.Ordinal) ? json : text);
    if (results.Any(r => r.Blocking && r.Status == EnvironmentCheckStatuses.Fail)) return 2;
    if (results.Any(r => r.Status is EnvironmentCheckStatuses.Warning or EnvironmentCheckStatuses.NotVerifiable)) return 1;
    return 0;
}
catch (JsonException exception) { Console.Error.WriteLine($"Configuração inválida: {exception.GetType().Name}"); return 3; }
catch (Exception exception) { Console.Error.WriteLine($"Erro inesperado seguro: {exception.GetType().Name}"); return 4; }
