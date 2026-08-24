using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using InovaGed.Environment.Doctor.Quality;

namespace InovaGed.Environment.Doctor.Checks;

public sealed class RouteSmokeQualityCheck : IQualityCheck
{
    private static readonly HashSet<HttpStatusCode> AcceptedStatuses =
    [
        HttpStatusCode.OK,
        HttpStatusCode.Found,
        HttpStatusCode.Unauthorized,
        HttpStatusCode.Forbidden
    ];

    private static readonly string[] FatalResponseMarkers =
    [
        "RuntimeCompilation",
        "DatabaseSchemaException",
        "A suitable constructor",
        "materialization",
        "Unable to resolve service"
    ];

    public string Name => "route-smoke";

    public async Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext context, CancellationToken cancellationToken)
    {
        var routesFile = Path.Combine(context.Root, "InovaGed.Environment.Doctor", "quality-routes.json");
        var routes = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(routesFile, cancellationToken)) ?? [];
        var output = Path.Combine(context.Root, "artifacts", "quality-gate");
        Directory.CreateDirectory(output);

        if (string.IsNullOrWhiteSpace(context.BaseUrl))
        {
            var skipped = routes.Select(route => new RouteSmokeResult(route, null, 0, false, "Aplicação não iniciada")).ToArray();
            await WriteReportsAsync(output, context.BaseUrl, skipped, cancellationToken);
            return [new(Name, QualityStatus.Warning, $"{routes.Length} rotas inventariadas; execução HTTP pendente.",
                "O relatório foi gerado sem chamadas de rede.", "Defina QUALITY_GATE_BASE_URL para executar o smoke test ativo.",
                Resource: "artifacts/quality-gate/route-smoke-report.md")];
        }

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AllowAutoRedirect = false
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var results = new List<RouteSmokeResult>();

        foreach (var route in routes)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await http.GetAsync(context.BaseUrl.TrimEnd('/') + route, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var marker = FatalResponseMarkers.FirstOrDefault(value => body.Contains(value, StringComparison.OrdinalIgnoreCase));
                var passed = AcceptedStatuses.Contains(response.StatusCode) && marker is null;
                results.Add(new(route, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, passed,
                    marker is null ? null : $"Assinatura de erro detectada: {marker}"));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                results.Add(new(route, null, stopwatch.ElapsedMilliseconds, false, $"{exception.GetType().Name}: {exception.Message}"));
            }
        }

        await WriteReportsAsync(output, context.BaseUrl, results, cancellationToken);
        return results.Select(result => new QualityFinding(Name, result.Passed ? QualityStatus.Pass : QualityStatus.Fail,
            $"{result.Route} -> {(result.StatusCode?.ToString() ?? "sem resposta")} ({result.ElapsedMilliseconds} ms)",
            result.Passed ? null : result.Error ?? "Status fora de 200, 302, 401 e 403.",
            result.Passed ? null : "Consultar o log correlacionado e corrigir a causa raiz.",
            Resource: result.Route)).ToArray();
    }

    private static async Task WriteReportsAsync(string output, string? baseUrl, IReadOnlyCollection<RouteSmokeResult> results, CancellationToken ct)
    {
        var report = new RouteSmokeReport(DateTimeOffset.UtcNow, baseUrl ?? "não configurada", results);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await File.WriteAllTextAsync(Path.Combine(output, "route-smoke-report.json"), JsonSerializer.Serialize(report, jsonOptions), ct);

        var markdown = new StringBuilder("# Route smoke report\n\n")
            .AppendLine($"- **Gerado em (UTC):** {report.GeneratedAtUtc:O}")
            .AppendLine($"- **Base URL:** {report.BaseUrl}")
            .AppendLine($"- **Resultado:** {(results.All(item => item.Passed) ? "PASS" : "INCOMPLETO/FALHA")}")
            .AppendLine("\n| Rota | HTTP | Tempo (ms) | Resultado | Detalhe |")
            .AppendLine("|---|---:|---:|---|---|");
        foreach (var item in results)
            markdown.AppendLine($"| {item.Route} | {item.StatusCode?.ToString() ?? "-"} | {item.ElapsedMilliseconds} | {(item.Passed ? "PASS" : "FAIL")} | {item.Error?.Replace("|", "\\|") ?? "-"} |");
        await File.WriteAllTextAsync(Path.Combine(output, "route-smoke-report.md"), markdown.ToString(), ct);
    }

    private sealed record RouteSmokeReport(DateTimeOffset GeneratedAtUtc, string BaseUrl, IReadOnlyCollection<RouteSmokeResult> Routes);
    private sealed record RouteSmokeResult(string Route, int? StatusCode, long ElapsedMilliseconds, bool Passed, string? Error);
}
