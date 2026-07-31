using System.Text.RegularExpressions;
using Xunit;

namespace InovaGed.UiTests;

internal static class RecoverySource
{
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    internal static string Read(string path)
    {
        return File.ReadAllText(Path.Combine(Root(), path));
    }
}

public sealed class AppShellRecoveryTests
{
    [Fact]
    public void Layout_is_a_small_composition_root()
    {
        var layout = RecoverySource.Read("InovaGed.Web/Views/Shared/_Layout.cshtml");
        Assert.True(layout.Split('\n').Length < 170);
        Assert.Contains("AppShell/_Sidebar", layout);
        Assert.Contains("AppShell/_Topbar", layout);
        Assert.DoesNotContain("assistantDrawer", layout);
        Assert.DoesNotContain("appCommandPalette", layout);
    }
}

public sealed class SidebarRecoveryTests
{
    [Fact]
    public void Sidebar_is_flat_and_historical_width()
    {
        var menu = RecoverySource.Read("InovaGed.Web/Views/Shared/AppShell/_Menu.cshtml");
        var css = RecoverySource.Read("InovaGed.Web/wwwroot/css/inovaged.shell.css");
        Assert.DoesNotContain("<details", menu, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<summary", menu, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("width: 260px", css);
        Assert.Contains("sidebar-footer", css);
    }

    [Fact]
    public void Menu_does_not_use_reserved_razor_section_identifier()
    {
        var menu = RecoverySource.Read(
            "InovaGed.Web/Views/Shared/AppShell/_Menu.cshtml");

        Assert.DoesNotContain(
            "@section.",
            menu,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "var section",
            menu,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "GetHashCode()",
            menu,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_sections_have_stable_accessible_ids()
    {
        var menu = RecoverySource.Read(
            "InovaGed.Web/Views/Shared/AppShell/_Menu.cshtml");

        Assert.Contains(
            "sidebar-menu-section-",
            menu);

        Assert.Contains(
            "aria-labelledby",
            menu);
    }
}

public sealed class TopbarRecoveryTests
{
    [Fact]
    public void Topbar_has_title_subtitle_and_primary_action()
    {
        var source = RecoverySource.Read(
            "InovaGed.Web/Views/Shared/AppShell/_Topbar.cshtml");

        Assert.Contains(
            "Model.PageTitle",
            source);

        Assert.Contains(
            "Model.PageSubtitle",
            source);

        Assert.Contains(
            "Model.PrimaryAction",
            source);
    }
}

public sealed class MobileNavigationRecoveryTests
{
    [Fact]
    public void Mobile_navigation_closes_and_restores_focus()
    {
        var source = RecoverySource.Read("InovaGed.Web/wwwroot/js/app-shell.js");
        Assert.Contains("offcanvas.hide()", source);
        Assert.Contains("opener?.focus()", source);
    }
}

public sealed class FeedbackRecoveryTests
{
    [Fact]
    public void Feedback_nodes_are_unique_and_cover_all_tempdata_types()
    {
        var views = Directory.GetFiles(
                Path.Combine(RecoverySource.Root(), "InovaGed.Web", "Views"),
                "*.cshtml",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Equal(
            1,
            views.Sum(source => Regex.Matches(source, "id=\"appToastContainer\"").Count));

        Assert.Equal(
            1,
            views.Sum(source => Regex.Matches(source, "id=\"appConfirmModal\"").Count));

        var feedback = RecoverySource.Read(
            "InovaGed.Web/Views/Shared/Feedback/_FeedbackLayer.cshtml");

        foreach (var type in new[] { "Success", "Info", "Warning", "Error" })
        {
            Assert.Contains($"TempData[\"{type}\"]", feedback);
        }
    }
}

public sealed class MenuRouteRecoveryTests
{
    [Fact]
    public void Every_visible_menu_controller_exists()
    {
        var service = RecoverySource.Read(
            "InovaGed.Web/Services/UserShellContextService.cs");

        var controllers = Regex.Matches(
                service,
                "Item\\(\"[^\"]+\", \"([^\"]+)\", \"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct();

        foreach (var controller in controllers)
        {
            Assert.True(
                new[] { "Controller", "Controllers" }.Any(folder =>
                    File.Exists(Path.Combine(
                        RecoverySource.Root(),
                        "InovaGed.Web",
                        folder,
                        controller + "Controller.cs"))),
                $"Controller ausente: {controller}");
        }
    }
}

public sealed class LegacyParityTests
{
    [Fact]
    public void Page_styles_cannot_override_shell_selectors()
    {
        var forbidden = new Regex(
            @"(^|[},\s])(body|\.wrapper|\.app-shell|\.sidebar|\.app-sidebar|\.main|\.app-main|\.topbar|\.app-topbar)\s*\{",
            RegexOptions.Multiline);

        foreach (var file in Directory.GetFiles(
                     Path.Combine(RecoverySource.Root(), "InovaGed.Web", "wwwroot", "css", "pages"),
                     "*.css"))
        {
            Assert.False(
                forbidden.IsMatch(File.ReadAllText(file)),
                $"Seletor estrutural em {Path.GetFileName(file)}");
        }
    }
}
