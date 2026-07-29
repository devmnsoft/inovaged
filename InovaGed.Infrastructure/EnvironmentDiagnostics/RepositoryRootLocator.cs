using InovaGed.Application.EnvironmentDiagnostics;

namespace InovaGed.Infrastructure.EnvironmentDiagnostics;

public sealed class RepositoryRootLocator(IEnvironmentContext environment) : IRepositoryRootLocator
{
    public RepositoryRootResult Locate(string? explicitPath = null)
    {
        var candidates = new[]
        {
            (explicitPath, "explicit"), (environment.BaseDirectory, "base-directory"),
            (environment.CurrentDirectory, "current-directory"),
            (environment.GetEnvironmentVariable("INOVAGED_REPOSITORY_ROOT"), "configured-variable")
        };
        foreach (var (path, source) in candidates)
        {
            var found = Find(path);
            if (found is not null) return new(true, found, source, Evidence(found));
        }
        return new(false, null, "not-found", []);
    }
    private static string? Find(string? start)
    {
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start)) return null;
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "InovaGed.sln")) &&
                File.Exists(Path.Combine(current.FullName, "global.json")) &&
                File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))) return current.FullName;
            current = current.Parent;
        }
        return null;
    }
    private static string[] Evidence(string root) => new[] { "InovaGed.sln", "global.json", "Directory.Build.props" }
        .Where(file => File.Exists(Path.Combine(root, file))).ToArray();
}
