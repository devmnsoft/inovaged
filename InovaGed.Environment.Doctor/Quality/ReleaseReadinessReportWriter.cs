using System.Net;
using System.Text;
using System.Text.Json;

namespace InovaGed.Environment.Doctor.Quality;

public static class ReleaseReadinessReportWriter
{
    public static async Task WriteAsync(string root, QualityGateReport report, CancellationToken ct)
    {
        var directory = Path.Combine(root, "artifacts", "release");
        Directory.CreateDirectory(directory);
        var failures = report.Checks.Where(x => x.Status == QualityStatus.Fail).ToArray();
        var warnings = report.Checks.Where(x => x.Status == QualityStatus.Warning).ToArray();
        var groups = report.Checks.GroupBy(x => x.Check).OrderBy(x => x.Key).ToArray();
        var payload = new
        {
            name = "Release Candidate Readiness",
            generatedAtUtc = report.GeneratedAtUtc,
            status = report.Status.ToString().ToUpperInvariant(),
            buildStatus = report.Checks.FirstOrDefault(x => x.Check == "Build")?.Status.ToString() ?? "NotVerified",
            migrationsPending = report.Checks.Where(x => x.Check.Contains("migration", StringComparison.OrdinalIgnoreCase) && x.Status != QualityStatus.Pass).Select(x => x.Message),
            criticalIncidents = report.Checks.Where(x => x.Check.Contains("incident", StringComparison.OrdinalIgnoreCase) && x.Status == QualityStatus.Fail).Select(x => x.Message),
            route500 = report.Checks.Where(x => x.Check.Contains("route", StringComparison.OrdinalIgnoreCase) && x.Status == QualityStatus.Fail).Select(x => x.Message),
            permissionsMissing = report.Checks.Where(x => x.Message.Contains("permiss", StringComparison.OrdinalIgnoreCase) && x.Status != QualityStatus.Pass).Select(x => x.Message),
            iconRisks = report.Checks.Where(x => x.Check.Contains("icon", StringComparison.OrdinalIgnoreCase) && x.Status != QualityStatus.Pass).Select(x => x.Message),
            razorRisks = report.Checks.Where(x => x.Check.Contains("razor", StringComparison.OrdinalIgnoreCase) && x.Status != QualityStatus.Pass).Select(x => x.Message),
            dapperRisks = report.Checks.Where(x => x.Check.Contains("dapper", StringComparison.OrdinalIgnoreCase) && x.Status != QualityStatus.Pass).Select(x => x.Message),
            actions = report.Checks.Where(x => x.Status != QualityStatus.Pass).Select(x => x.Action).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(),
            checks = report.Checks
        };
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        await File.WriteAllTextAsync(Path.Combine(directory, "release-readiness-report.json"), JsonSerializer.Serialize(payload, jsonOptions), ct);

        var md = new StringBuilder($"# Release Candidate Readiness\n\n- **Status geral da release:** {report.Status.ToString().ToUpperInvariant()}\n- **Gerado em (UTC):** {report.GeneratedAtUtc:O}\n- **Falhas bloqueantes:** {failures.Length}\n- **Alertas:** {warnings.Length}\n\n## Resultado por verificação\n\n| Verificação | Status | Mensagem | Ação recomendada |\n|---|---|---|---|\n");
        foreach (var finding in report.Checks) md.AppendLine($"| {Escape(finding.Check)} | {finding.Status.ToString().ToUpperInvariant()} | {Escape(finding.Message)} | {Escape(finding.Action)} |");
        md.AppendLine("\n## Cobertura\n\nBuild, migrations, incidentes críticos, rotas 500, permissões, ícones, Razor, Dapper, DI, schema e links administrativos são consolidados acima. O catálogo funcional de módulos é apresentado em `/ReleaseReadiness/Modules`.\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "release-readiness-report.md"), md.ToString(), ct);
        var rows = string.Join("", report.Checks.Select(x => $"<tr><td>{H(x.Check)}</td><td class='{x.Status}'>{x.Status}</td><td>{H(x.Message)}</td><td>{H(x.Action)}</td></tr>"));
        var html = $"<!doctype html><html lang='pt-BR'><meta charset='utf-8'><title>Release Candidate Readiness</title><style>body{{font:14px system-ui;margin:2rem;color:#172033}}table{{border-collapse:collapse;width:100%}}th,td{{padding:.65rem;border:1px solid #d8deea;text-align:left}}.Pass{{color:#18794e}}.Warning{{color:#9a6700}}.Fail{{color:#b42318}}</style><h1>Release Candidate Readiness</h1><p>Status geral: <strong class='{report.Status}'>{report.Status}</strong> · {report.GeneratedAtUtc:O}</p><table><thead><tr><th>Verificação</th><th>Status</th><th>Mensagem</th><th>Ação</th></tr></thead><tbody>{rows}</tbody></table></html>";
        await File.WriteAllTextAsync(Path.Combine(directory, "release-readiness-report.html"), html, ct);
    }

    private static string Escape(string? value) => (value ?? "—").Replace("|", "\\|").Replace("\n", " ");
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "—");
}
