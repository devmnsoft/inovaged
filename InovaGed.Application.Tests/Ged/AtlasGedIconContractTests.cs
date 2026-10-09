using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests.Ged;

public sealed class AtlasGedIconContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Registry_sprite_and_ged_static_icons_are_consistent()
    {
        var registry = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/Services/AtlasIconRegistry.cs"));
        var sprite = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/Views/Shared/Icons/_AtlasIconSprite.cshtml"));
        var names = Regex.Matches(registry, "new\\(\"([^\"]+)\",\\s*\"([^\"]+)\"")
            .Select(m => (Name: m.Groups[1].Value, Symbol: m.Groups[2].Value)).ToArray();
        var symbols = Regex.Matches(sprite, "<symbol\\s+id=\"([^\"]+)\"[^>]*viewBox=\"([^\"]+)\"")
            .Select(m => (Id: m.Groups[1].Value, ViewBox: m.Groups[2].Value)).ToArray();

        Assert.NotEmpty(names);
        Assert.Empty(symbols.GroupBy(x => x.Id).Where(x => x.Count() > 1));
        Assert.All(names, icon => Assert.Contains(symbols, symbol => symbol.Id == icon.Symbol));
        Assert.All(symbols, symbol => Assert.Matches(@"^0 0 [1-9]\d*(?:\.\d+)? [1-9]\d*(?:\.\d+)?$", symbol.ViewBox));

        var registered = names.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gedViews = Directory.GetFiles(Path.Combine(Root, "InovaGed.Web/Views/Ged"), "*.cshtml", SearchOption.AllDirectories);
        var used = gedViews.SelectMany(path => Regex.Matches(File.ReadAllText(path), "<app-icon[^>]+name=\"([^@\"]+)\"")
            .Select(match => match.Groups[1].Value));
        Assert.All(used, name => Assert.Contains(name, registered));
    }

    [Fact]
    public void Ged_views_do_not_depend_on_bootstrap_icons()
    {
        var folder = Path.Combine(Root, "InovaGed.Web/Views/Ged");
        var bootstrapIcon = new Regex(@"\bbi-[a-z0-9-]+", RegexOptions.IgnoreCase);
        Assert.All(Directory.GetFiles(folder, "*.cshtml", SearchOption.AllDirectories), path =>
            Assert.DoesNotMatch(bootstrapIcon, File.ReadAllText(path)));
    }

    [Fact]
    public void Ged_layout_has_one_canonical_stylesheet()
    {
        var workspace = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/css/pages/ged-workspace.css"));
        var components = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/css/ged-explorer.css"));
        var index = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/Views/Ged/Index.cshtml"));

        Assert.Contains(".ged-page", workspace);
        Assert.Contains("container: ged-workspace / inline-size", workspace);
        Assert.DoesNotContain(".ged-page", components);
        Assert.DoesNotContain("<style", index, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-ged-open-folders", index);
    }

    [Fact]
    public void Ged_workspace_scroll_locks_match_runtime_classes()
    {
        var shell = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/css/inovaged.shell.css"));
        var sidePanel = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/js/ged-document-side-panel.js"));
        var explorer = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/js/ged-explorer-controller.js"));

        Assert.DoesNotContain("document.body.classList.add('ged-preview-open')", sidePanel);
        Assert.Contains("document.body.classList.toggle('ged-reader-focus-open'", sidePanel);
        Assert.Contains("body.ged-reader-focus-open", shell);
        Assert.Contains("document.body.classList.toggle(\"ged-drawer-lock\"", explorer);
        Assert.Contains("body.ged-drawer-lock", shell);
        Assert.DoesNotContain("body.ged-preview-open", shell);
    }

    [Fact]
    public void Ged_panel_interactions_restore_focus_and_use_canonical_loader()
    {
        var sidePanel = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/js/ged-document-side-panel.js"));
        var drawer = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/js/ged-explorer-controller.js"));
        var preview = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/js/ged-document-preview.js"));

        Assert.Contains("lastPanelTrigger.focus", sidePanel);
        Assert.Contains("window.scrollTo(window.scrollX", sidePanel);
        Assert.Contains("folderDrawerTrigger?.focus", drawer);
        Assert.Contains("window.openGedDocumentPanel(row.dataset.documentId", preview);
    }

    [Fact]
    public void Ged_component_styles_do_not_override_workspace_geometry()
    {
        var components = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/wwwroot/css/ged-explorer.css"));

        Assert.DoesNotContain("max-height:calc(100vh - 220px)", components);
        Assert.DoesNotContain(".ged-page", components);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("InovaGed.sln não encontrado.");
    }
}
