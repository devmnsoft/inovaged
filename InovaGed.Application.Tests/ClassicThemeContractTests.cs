using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests;

public sealed class ClassicThemeContractTests
{
    [Fact]
    [Trait("Category", "VisualContract")]
    public void Classic_is_the_single_default_theme_and_has_the_brand_contract()
    {
        var layout = Read("InovaGed.Web/Views/Shared/_Layout.cshtml");
        var auth = Read("InovaGed.Web/Views/Shared/_LayoutAuth.cshtml");
        var theme = Read("InovaGed.Web/wwwroot/css/themes/inovaged-classic.css");

        Assert.Contains("data-theme=\"inovaged-classic\"", layout);
        Assert.Contains("data-theme=\"inovaged-classic\"", auth);
        Assert.Contains("--ig-primary-600: #2563eb", theme);
        Assert.Contains("--ig-accent-500: #22c55e", theme);
        Assert.Contains("linear-gradient(135deg, #2563eb 0%, #22c55e 100%)", theme);
        Assert.DoesNotMatch(new Regex("(purple|violet|#7c3aed|#8b5cf6)", RegexOptions.IgnoreCase), theme);
    }

    internal static string Read(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
