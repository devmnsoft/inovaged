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
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
