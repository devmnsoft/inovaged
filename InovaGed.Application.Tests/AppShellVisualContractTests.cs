using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests;

public sealed class AppShellVisualContractTests
{
    [Fact]
    [Trait("Category", "VisualContract")]
    public void Shell_has_one_sidebar_one_topbar_and_one_logout()
    {
        var rendered = string.Join("\n", ExpandPartials("InovaGed.Web/Views/Shared/_Layout.cshtml").Select(Read));
        Assert.Equal(1, Regex.Matches(rendered, @"<aside\s+class=""sidebar").Count);
        Assert.Equal(1, Regex.Matches(rendered, @"<header\s+class=""[^""]*topbar").Count);
        Assert.Equal(1, Regex.Matches(rendered, @"asp-action=""Logout""").Count);
    }

    private static IReadOnlyList<string> ExpandPartials(string start)
    {
        var pending = new Queue<string>();
        var seen = new List<string>();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var relative = pending.Dequeue();
            if (seen.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
            if (!File.Exists(Find(relative))) continue;
            seen.Add(relative);
            foreach (Match match in Regex.Matches(Read(relative), @"<partial\s+name=""([^""]+)"""))
            {
                var name = match.Groups[1].Value.Replace('\\', '/');
                if (!name.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)) name += ".cshtml";
                pending.Enqueue($"InovaGed.Web/Views/Shared/{name}");
            }
        }
        return seen;
    }

    private static string Read(string relative) => File.ReadAllText(Find(relative));

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
