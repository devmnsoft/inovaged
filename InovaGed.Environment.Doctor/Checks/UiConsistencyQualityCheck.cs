using System.Text.Json;
using System.Text.RegularExpressions;
using InovaGed.Environment.Doctor.Quality;

namespace InovaGed.Environment.Doctor.Checks;

public sealed partial class UiConsistencyQualityCheck : IQualityCheck
{
    public string Name => "UI Consistency";

    public async Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext context, CancellationToken cancellationToken)
    {
        var viewsRoot = Path.Combine(context.Root, "InovaGed.Web", "Views");
        var issues = new List<UiIssue>();
        foreach (var path in Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(context.Root, path).Replace('\\', '/');
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            AddMatches(relative, text, RawTable(), "raw-table", "Tabela Bootstrap sem atlas-table.", issues);
            AddMatches(relative, text, InlineTupleLoop(), "razor-inline-tuples", "@foreach contém array/tuplas inline.", issues);
            AddMatches(relative, text, UnescapedMedia(), "razor-media", "@media não escapado em Razor.", issues);
            AddMatches(relative, text, RawCard(), "raw-card", "Card simples sem wrapper Atlas/ig.", issues);

            if (relative.Contains("/Views/Administration/", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(relative).StartsWith('_')
                && !text.Contains("PageHero", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("PageHeader", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("admin-hero", StringComparison.OrdinalIgnoreCase))
                issues.Add(new(relative, "administration-hero", "View administrativa sem PageHero.", 1));

            if (relative.Contains("/Views/Labels/", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("atlas", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("ig-label", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("label-", StringComparison.OrdinalIgnoreCase))
                issues.Add(new(relative, "labels-premium", "View de etiquetas sem classe Atlas/ig-label.", 1));
        }

        var output = Path.Combine(context.Root, "artifacts", "ui");
        Directory.CreateDirectory(output);
        var payload = new { generatedAtUtc = DateTimeOffset.UtcNow, issueCount = issues.Count, issues };
        await File.WriteAllTextAsync(Path.Combine(output, "ui-consistency-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        var markdown = new List<string> { "# UI consistency report", "", $"Gerado em: {payload.generatedAtUtc:O}", $"Ocorrências: **{issues.Count}**", "", "| Regra | View | Linha | Mensagem |", "|---|---|---:|---|" };
        markdown.AddRange(issues.Select(x => $"| `{x.Rule}` | `{x.View}` | {x.Line} | {x.Message} |"));
        if (issues.Count == 0) markdown.Add("| — | — | — | Nenhuma inconsistência detectada. |");
        await File.WriteAllLinesAsync(Path.Combine(output, "ui-consistency-report.md"), markdown, cancellationToken);

        var findings = new List<QualityFinding>
        {
            new(Name, QualityStatus.Pass, "Relatórios gerados em artifacts/ui.", Resource: "artifacts/ui/ui-consistency-report.md")
        };
        if (issues.Count > 0)
            findings.Add(new(Name, QualityStatus.Warning, $"{issues.Count} ocorrência(s) visual(is) legada(s) encontrada(s).", "Inconsistências podem reaparecer em views ainda não migradas.", "Priorizar rotas críticas listadas no relatório.", Resource: "artifacts/ui/ui-consistency-report.json"));
        else
            findings.Add(new(Name, QualityStatus.Pass, "Views analisadas sem riscos visuais conhecidos."));
        var iconFindings = await new IconQualityCheck().RunAsync(context, cancellationToken);
        findings.AddRange(iconFindings.Where(x => x.Status != QualityStatus.Pass));
        return findings;
    }

    private static void AddMatches(string view, string text, Regex pattern, string rule, string message, ICollection<UiIssue> issues)
    {
        foreach (Match match in pattern.Matches(text))
            issues.Add(new(view, rule, message, text.AsSpan(0, match.Index).Count('\n') + 1));
    }

    [GeneratedRegex("class\\s*=\\s*[\\\"'][^\\\"']*\\btable\\b(?![^\\\"']*\\batlas-table\\b)[^\\\"']*[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex RawTable();
    [GeneratedRegex("@foreach\\s*\\([^)]*new\\s*\\[\\]", RegexOptions.IgnoreCase)]
    private static partial Regex InlineTupleLoop();
    [GeneratedRegex("(?m)^\\s*@media\\b")]
    private static partial Regex UnescapedMedia();
    [GeneratedRegex("class\\s*=\\s*[\\\"'][^\\\"']*\\bcard\\b(?![^\\\"']*(?:atlas|ig-))[^\\\"']*[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex RawCard();

    private sealed record UiIssue(string View, string Rule, string Message, int Line);
}
