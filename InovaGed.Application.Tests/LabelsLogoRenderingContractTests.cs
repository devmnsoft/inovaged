namespace InovaGed.Application.Tests;

using InovaGed.Web.Models.Branding;

public sealed class LabelsLogoRenderingContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Labels_logo_rendering_contract_is_safe_and_print_action_is_direct()
    {
        var partial = Read("InovaGed.Web/Views/Shared/Branding/_PrintLogo.cshtml");
        Assert.Contains("Model.ImageLoaded", partial);
        Assert.Contains("ImageDataUriValidator.IsValidImageDataUri", partial);
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

    [Fact]
    public void Loaded_logo_is_mapped_to_an_embedded_image_source()
    {
        var resolved = new ResolvedPrintLogo(Guid.NewGuid(), "Marca", null,
            "data:image/png;base64,AQID", "Logo oficial", 38, null, true,
            "CONTAIN", "TOP_LEFT", 0, 0, true, true, null);

        var result = PrintLogoViewModelMapper.FromResolved(resolved);

        Assert.True(result.HasLogo);
        Assert.True(result.ImageLoaded);
        Assert.StartsWith("data:image/", result.PrintImageSource);
    }

    [Fact]
    public void Missing_logo_file_is_mapped_without_a_broken_image_source()
    {
        var resolved = new ResolvedPrintLogo(Guid.NewGuid(), "Marca", null, null,
            "Logo oficial", 38, null, true, "CONTAIN", "TOP_LEFT", 0, 0,
            true, false, "A logo selecionada não pôde ser carregada.");

        var result = PrintLogoViewModelMapper.FromResolved(resolved);

        Assert.True(result.HasLogo);
        Assert.False(result.ImageLoaded);
        Assert.Null(result.PrintImageSource);
        Assert.False(string.IsNullOrWhiteSpace(result.LoadError));
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(Root,path));
    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName,"InovaGed.sln"))) current=current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Solution root not found.");
    }
}
