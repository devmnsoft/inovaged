using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests;

public sealed class PremiumUiContractTests
{
    [Fact]
    [Trait("Category", "VisualContract")]
    public void Shell_has_one_structural_owner_and_restored_light_identity()
    {
        var shell = ClassicThemeContractTests.Read("InovaGed.Web/wwwroot/css/inovaged.shell.css");
        var tokens = ClassicThemeContractTests.Read("InovaGed.Web/wwwroot/css/inovaged.tokens.css");
        Assert.Matches(@"\.app-shell\s*\{[^}]*display:flex", shell);
        Assert.Matches(@"\.app-main\s*\{[^}]*flex:1 1 auto", shell);
        Assert.Matches(@"\.app-sidebar\s*\{[^}]*background:var\(--ig-surface\)", shell);
        Assert.Matches(@"\.sidebar-menu a\.active\s*\{[^}]*background:var\(--ig-primary-600\)", shell);
        Assert.Contains("--ig-brand-gradient:linear-gradient(135deg,#2563eb,#22c55e)", tokens);
    }

    [Fact]
    [Trait("Category", "VisualContract")]
    public void Global_layout_loads_canonical_styles_in_contract_order()
    {
        var layout = ClassicThemeContractTests.Read("InovaGed.Web/Views/Shared/_Layout.cshtml");
        var names = new[] { "bootstrap.min.css", "bootstrap-icons.min.css", "inovaged.tokens.css", "inovaged.base.css", "inovaged.shell.css", "inovaged.components.css", "RenderSectionAsync(\"Styles\"", "inovaged.utilities.css" };
        var cursor = -1;
        foreach (var name in names) { var next = layout.IndexOf(name, cursor + 1, StringComparison.Ordinal); Assert.True(next > cursor, $"Missing or out of order: {name}"); cursor = next; }
        Assert.DoesNotContain("pages/administration.css", layout);
        Assert.DoesNotContain("https://cdn", layout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "VisualContract")]
    public void Page_styles_do_not_own_global_shell()
    {
        var root = Path.GetDirectoryName(Find("InovaGed.Web/InovaGed.Web.csproj"))!;
        var forbidden = new Regex(@"(^|[},])\s*(body|\.app-shell|\.app-sidebar|\.sidebar|\.app-topbar|\.topbar)\b", RegexOptions.Multiline);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "wwwroot/css/pages"), "*.css")) Assert.DoesNotMatch(forbidden, File.ReadAllText(file));
    }

    private static string Find(string relative) { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null) { var candidate = Path.Combine(directory.FullName, relative); if (File.Exists(candidate)) return candidate; directory = directory.Parent; } throw new FileNotFoundException(relative); }
}
