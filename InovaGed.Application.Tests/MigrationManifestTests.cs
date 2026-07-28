using System.Text.Json;
namespace InovaGed.Application.Tests;
public sealed class MigrationManifestTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void Manifest_has_unique_ordered_existing_migrations()
    {
        var root = FindRoot(); using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "database", "migrations.manifest.json")));
        var migrations = json.RootElement.GetProperty("migrations").EnumerateArray().ToArray();
        var ids = migrations.Select(item => item.GetProperty("id").GetString()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(migrations, item => Assert.True(File.Exists(Path.Combine(root, item.GetProperty("path").GetString()!))));
        Assert.Equal("2026_07_backup_continuity_portability", ids[^2]);
        Assert.Equal("2026_07_estabilizar_admin_continuity_ci", ids[^1]);
    }
    private static string FindRoot() { var current = new DirectoryInfo(AppContext.BaseDirectory); while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln"))) current = current.Parent; return current?.FullName ?? throw new DirectoryNotFoundException(); }
}
