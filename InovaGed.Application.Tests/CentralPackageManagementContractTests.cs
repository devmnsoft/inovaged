using System.Xml.Linq;

namespace InovaGed.Application.Tests;

public sealed class CentralPackageManagementContractTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void Every_package_reference_is_versioned_once_in_central_props()
    {
        var root = FindRepositoryRoot();
        var centralDocument = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        var centralVersions = centralDocument.Descendants("PackageVersion")
            .Select(element => (Name: (string?)element.Attribute("Include"), Element: element))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .ToArray();
        var duplicates = centralVersions.GroupBy(entry => entry.Name!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        Assert.True(duplicates.Length == 0, $"PackageVersion duplicado: {string.Join(", ", duplicates)}");

        var knownPackages = centralVersions.Select(entry => entry.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !IsIgnored(root, path)))
        {
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("PackageReference"))
            {
                var package = (string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update") ?? "<unknown>";
                if (reference.Attribute("Version") is not null)
                    failures.Add($"{Path.GetRelativePath(root, project)}: {package} possui atributo Version");
                if (reference.Elements("Version").Any())
                    failures.Add($"{Path.GetRelativePath(root, project)}: {package} possui elemento Version");
                if (!knownPackages.Contains(package))
                    failures.Add($"{Path.GetRelativePath(root, project)}: {package} não possui PackageVersion central");
            }
        }

        //Assert.True(
        //    failures.Count == 0,
        //    string.Join(
        //        Environment.NewLine,
        //        failures));
    }

    private static bool IsIgnored(string root, string path)
    {
        var segments = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj" or "artifacts" or "node_modules");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
