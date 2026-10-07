using Xunit;

namespace InovaGed.Application.Tests;

public sealed class AtlasPrimeReportsContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Layout_loads_centralized_atlas_prime_design_system()
    {
        var layout = Read("InovaGed.Web/Views/Shared/_Layout.cshtml");
        Assert.Contains("css/atlas/atlas-prime.css", layout);
        Assert.True(File.Exists(Path.Combine(Root, "InovaGed.Web/Views/Shared/Atlas/_PageHeader.cshtml")));
        Assert.True(File.Exists(Path.Combine(Root, "InovaGed.Web/Views/Shared/Atlas/_PageMetrics.cshtml")));
        Assert.True(File.Exists(Path.Combine(Root, "InovaGed.Web/Views/Shared/Atlas/_DataState.cshtml")));
    }

    [Fact]
    public void Operational_form_controls_use_semantic_colors_with_accessible_text_contrast_across_states()
    {
        var tokens = Read("InovaGed.Web/wwwroot/css/atlas/atlas-tokens.css");
        var components = Read("InovaGed.Web/wwwroot/css/atlas/atlas-components.css");
        var layout = Read("InovaGed.Web/Views/Shared/_Layout.cshtml");
        var border = ReadToken(tokens, "--ig-border-strong");
        var surface = ReadToken(tokens, "--ig-surface");
        var text = ReadToken(tokens, "--ig-text");
        var secondaryText = ReadToken(tokens, "--ig-text-secondary");
        var mutedText = ReadToken(tokens, "--ig-text-muted");
        var mutedSurface = ReadToken(tokens, "--ig-surface-muted");

        Assert.True(layout.IndexOf("bootstrap.min.css", StringComparison.Ordinal) < layout.IndexOf("atlas/atlas-components.css", StringComparison.Ordinal),
            "Shared Atlas control styles must load after Bootstrap.");
        Assert.Contains(".form-control, .form-select { color: var(--ig-text); border-color: var(--ig-border-strong); background-color: var(--ig-surface); }", components);
        Assert.Contains(".form-control:focus, .form-select:focus { color: var(--ig-text); border-color: var(--ig-primary-600); background-color: var(--ig-surface);", components);
        Assert.Contains(".form-control::placeholder { color: var(--ig-text-muted); opacity: 1; }", components);
        Assert.Contains(".form-control:disabled, .form-select:disabled { color: var(--ig-text-secondary);", components);
        Assert.Contains("background-color: var(--ig-surface-muted); opacity: 1;", components);
        Assert.Contains(".form-control[readonly] { color: var(--ig-text); background-color: var(--ig-surface); }", components);
        Assert.Contains(".form-control:-webkit-autofill", components);
        Assert.Contains(".form-select:-webkit-autofill", components);
        Assert.Contains(".form-control:autofill", components);
        Assert.Contains(".form-select:autofill", components);
        Assert.Contains("-webkit-text-fill-color: var(--ig-text); caret-color: var(--ig-text);", components);

        Assert.True(ContrastRatio(text, surface) >= 4.5,
            $"Form text contrast must be at least 4.5:1 (found {ContrastRatio(text, surface):F2}:1).");
        Assert.True(ContrastRatio(mutedText, surface) >= 4.5,
            $"Placeholder contrast must be at least 4.5:1 (found {ContrastRatio(mutedText, surface):F2}:1).");
        Assert.True(ContrastRatio(secondaryText, mutedSurface) >= 4.5,
            $"Disabled form text contrast must be at least 4.5:1 (found {ContrastRatio(secondaryText, mutedSurface):F2}:1).");
        Assert.True(ContrastRatio(border, surface) >= 3,
            $"Form border contrast against the control surface must be at least 3:1 (found {ContrastRatio(border, surface):F2}:1).");
    }

    [Fact]
    public void Reports_hub_has_real_routes_filters_and_no_dead_export_button()
    {
        var view = Read("InovaGed.Web/Views/Reports/Index.cshtml");
        var controller = Read("InovaGed.Web/Controller/ReportsController.cs");
        var script = Read("InovaGed.Web/wwwroot/js/reports-prime.js");

        Assert.Contains("IActionResult Index()", controller);
        Assert.Contains("data-reports-page", view);
        Assert.Contains("reportSearch", view);
        Assert.Contains("reportCategory", view);
        Assert.Contains("href=\"@report.Url\"", view);
        Assert.DoesNotContain("<button", view[view.IndexOf("Exportação disponível", StringComparison.Ordinal)..]);
        Assert.Contains("setTimeout(apply, 180)", script);
    }

    [Fact]
    public void New_prime_assets_avoid_native_dialogs_and_bootstrap_icons()
    {
        var paths = new[]
        {
            "InovaGed.Web/Views/Reports/Index.cshtml",
            "InovaGed.Web/wwwroot/js/reports-prime.js",
            "InovaGed.Web/Views/Shared/Atlas/_PageHeader.cshtml"
        };
        foreach (var path in paths)
        {
            var content = Read(path);
            Assert.DoesNotContain("alert(", content);
            Assert.DoesNotContain("confirm(", content);
            Assert.DoesNotContain("prompt(", content);
            Assert.DoesNotContain("bi-", content);
        }
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    private static string ReadToken(string stylesheet, string token) =>
        stylesheet.Split('\n')
            .Select(line => line.Trim())
            .First(line => line.StartsWith(token + ":", StringComparison.Ordinal))
            .Split(':', 2)[1]
            .Trim()
            .TrimEnd(';');

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + .05) /
               (Math.Min(firstLuminance, secondLuminance) + .05);
    }

    private static double RelativeLuminance(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(character => new string(character, 2)));

        double LinearChannel(int offset)
        {
            var channel = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255d;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }

        return .2126 * LinearChannel(0) + .7152 * LinearChannel(2) + .0722 * LinearChannel(4);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
