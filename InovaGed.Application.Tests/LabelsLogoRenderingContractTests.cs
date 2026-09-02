namespace InovaGed.Application.Tests;

public sealed class LabelsLogoRenderingContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Labels_logo_rendering_contract_is_safe_and_print_action_is_direct()
    {
        var partial = Read("InovaGed.Web/Views/Shared/Branding/_PrintLogo.cshtml");
        Assert.Contains("Model.ImageLoaded", partial);
        Assert.Contains("!string.IsNullOrWhiteSpace", partial);
        Assert.Contains("PrintImageSource", partial);

        var wizard = Read("InovaGed.Web/Views/Labels/PrintWizard.cshtml");
        Assert.Contains("asp-for=\"SelectedLogoAssetId\"", wizard);
        Assert.Contains("formaction=\"@Url.Action(\"Preview\", \"Labels\")\"", wizard);
        Assert.Contains("formaction=\"@Url.Action(\"Print\", \"Labels\")\"", wizard);

        var printViews = new[] { "LocDeskFolderLabel.cshtml", "LocDeskBoxLabel.cshtml", "LocDeskFolderHolLabel.cshtml" };
        foreach (var view in printViews)
        {
            var content = Read($"InovaGed.Web/Views/Labels/{view}");
            Assert.Contains("data-label-print-now", content);
            Assert.Contains("labels-print-page.js", content);
        }
        Assert.Contains("window.print();", Read("InovaGed.Web/wwwroot/js/labels-print-page.js"));
        Assert.Contains(".no-print", Read("InovaGed.Web/wwwroot/css/labels-print.css"));
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(Root,path));
    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName,"InovaGed.sln"))) current=current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Solution root not found.");
    }
}
