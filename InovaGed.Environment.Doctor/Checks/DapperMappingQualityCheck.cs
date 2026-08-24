using System.Text.RegularExpressions;
using InovaGed.Environment.Doctor.Quality;

namespace InovaGed.Environment.Doctor.Checks;

public sealed partial class DapperMappingQualityCheck : IQualityCheck
{
    public string Name => "Dapper Mapping";

    public async Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext context, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.EnumerateFiles(context.Root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .ToArray();
        var publicRecords = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in sourceFiles)
        {
            var source = await File.ReadAllTextAsync(file, cancellationToken);
            foreach (Match match in PublicRecordRegex().Matches(source))
                publicRecords.Add(match.Groups[1].Value);
        }

        var findings = new List<QualityFinding>();
        foreach (var file in sourceFiles)
        {
            var source = await File.ReadAllTextAsync(file, cancellationToken);
            foreach (Match match in DapperMaterializationRegex().Matches(source))
            {
                var type = match.Groups[1].Value;
                if (!publicRecords.Contains(type)) continue;
                var relative = Path.GetRelativePath(context.Root, file).Replace('\\', '/');
                findings.Add(new(Name, QualityStatus.Fail,
                    $"FAIL - Dapper Mapping | Arquivo: {relative} | Tipo: {type}",
                    "record público materializado diretamente pelo Dapper.",
                    "usar DbRow mutável interno e mapear manualmente.", Resource: relative));
            }
        }

        return findings.Count > 0
            ? findings
            : [new(Name, QualityStatus.Pass, "Nenhum record público é materializado diretamente pelo Dapper.")];
    }

    private static bool IsGeneratedOrBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\bpublic\s+(?:sealed\s+)?record(?:\s+class|\s+struct)?\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex PublicRecordRegex();

    [GeneratedRegex(@"\b(?:QueryAsync|QuerySingleAsync|QueryFirstAsync|QueryFirstOrDefaultAsync|QuerySingleOrDefaultAsync)\s*<\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)*([A-Za-z_][A-Za-z0-9_]*)\s*>", RegexOptions.CultureInvariant)]
    private static partial Regex DapperMaterializationRegex();
}
