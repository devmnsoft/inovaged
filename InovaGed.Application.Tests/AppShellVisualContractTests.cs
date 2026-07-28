using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests;

public sealed class AppShellVisualContractTests
{
    [Fact]
    [Trait("Category", "VisualContract")]
    public void Shell_has_one_sidebar_one_topbar_and_one_logout()
    {
        var layout = ClassicThemeContractTests.Read("InovaGed.Web/Views/Shared/_Layout.cshtml");
        Assert.Equal(1, Regex.Matches(layout, "<partial name=\"AppShell/_AppSidebar\"").Count);
        Assert.Equal(1, Regex.Matches(layout, "<header class=\"topbar app-topbar\"").Count);
        Assert.Equal(1, Regex.Matches(layout, "asp-action=\"Logout\"").Count);
    }

    [Fact]
    [Trait("Category", "VisualContract")]
    public void Page_styles_do_not_redefine_brand_tokens()
    {
        var root = Path.GetDirectoryName(Find("InovaGed.Web/InovaGed.Web.csproj"))!;
        foreach (var file in Directory.GetFiles(Path.Combine(root, "wwwroot/css/pages"), "*.css"))
            Assert.DoesNotMatch(new Regex("--ig-(primary|accent)-", RegexOptions.IgnoreCase), File.ReadAllText(file));
    }

    private static string Find(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var candidate = Path.Combine(directory.FullName, relative); if (File.Exists(candidate)) return candidate; directory = directory.Parent; }
        throw new FileNotFoundException(relative);
    }
}
