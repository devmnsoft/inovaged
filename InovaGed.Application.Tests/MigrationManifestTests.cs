using System.Text.Json;
namespace InovaGed.Application.Tests;
public sealed class MigrationManifestTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void Manifest_has_unique_existing_migrations_in_consolidated_script()
    {
        var root = FindRoot();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "database", "migrations.manifest.json")));
        var migrations = json.RootElement.GetProperty("migrations").EnumerateArray().ToArray();
        var ids = migrations.Select(item => item.GetProperty("id").GetString()).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(migrations, item => Assert.True(File.Exists(Path.Combine(root, item.GetProperty("path").GetString()!))));

        var consolidated = File.ReadAllText(Path.Combine(root, "database", "apply_all_required_migrations.sql"));
        foreach (var migration in migrations)
        {
            var relativePath = migration.GetProperty("path").GetString()!;
            var includePath = relativePath["database/".Length..].Replace('\\', '/');
            Assert.Equal(1, CountOccurrences(consolidated, $"\\ir {includePath}"));
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindRoot() { var current = new DirectoryInfo(AppContext.BaseDirectory); while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln"))) current = current.Parent; return current?.FullName ?? throw new DirectoryNotFoundException(); }
}
