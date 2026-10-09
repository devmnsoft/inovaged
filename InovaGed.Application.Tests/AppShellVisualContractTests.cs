using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests;

public sealed class AppShellVisualContractTests
{
    [Fact]
    [Trait("Category", "VisualContract")]
    public void Shell_has_one_sidebar_one_topbar_and_one_logout()
    {
        var layout = ClassicThemeContractTests.Read("InovaGed.Web/Views/Shared/_Layout.cshtml");
        Assert.True(Regex.IsMatch(layout, @"<partial\s+name=""AppShell/_(App)?Sidebar""") || Regex.IsMatch(layout, @"<aside\s+class=""sidebar"));
        Assert.True(Regex.IsMatch(layout, @"<header\s+class=""topbar\s+app-topbar""") || Regex.IsMatch(layout, @"<partial\s+name=""AppShell/_Topbar"""));
        Assert.True(Regex.IsMatch(layout, @"asp-action=""Logout""") || ClassicThemeContractTests.Read("InovaGed.Web/Views/Shared/AppShell/_Sidebar.cshtml").Contains("asp-action=\"Logout\""));
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
