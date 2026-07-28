namespace InovaGed.Application.Tests;

public sealed class AdministrationRazorCompilationTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void Administration_views_are_typed_and_do_not_use_reserved_section_variable()
    {
        var root = FindRoot();
        var views = new[] { "Index.cshtml", "_EnvironmentSummary.cshtml", "_ConfigurationRecommendations.cshtml", "_AdministrationSections.cshtml", "_AdministrationSection.cshtml", "_AdministrationActionCard.cshtml" };
        foreach (var view in views) Assert.True(File.Exists(Path.Combine(root, "InovaGed.Web", "Views", "Administration", view)), $"Missing {view}");
        var administrationView = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Views", "Administration", "Index.cshtml"));
        Assert.DoesNotContain("@foreach (var section ", administrationView, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@model", administrationView);
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}
