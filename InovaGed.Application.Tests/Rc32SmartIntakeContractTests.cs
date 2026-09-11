namespace InovaGed.Application.Tests;

public sealed class Rc32SmartIntakeContractTests
{
    [Theory]
    [InlineData("tree.code ilike", "classification_search_matches_code")]
    [InlineData("tree.title ilike", "classification_search_matches_name")]
    [InlineData("tree.description, '') ilike", "classification_search_matches_description")]
    public void Classification_search_uses_real_plan_fields(string contract, string scenario)
    {
        var source = Read("InovaGed.Web/Controller/GedController.cs");
        Assert.Contains(contract, source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code = string.Empty", source, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    [Fact]
    public void Classification_option_is_tenant_scoped()
    {
        var source = Read("InovaGed.Web/Controller/GedController.cs");
        Assert.Contains("n.tenant_id = @tenantId", source);
        Assert.Contains("tenantId = _currentUser.TenantId", source);
    }

    [Fact]
    public void New_model_wizard_validates_each_step()
    {
        var view = Read("InovaGed.Web/Views/Labels/Designer/New.cshtml");
        var script = Read("InovaGed.Web/wwwroot/js/labels-new-wizard.js");
        Assert.Equal(5, Count(view, "data-wizard-step"));
        Assert.Contains("checkValidity()", script);
        Assert.Contains("aria-current", script);
    }

    [Fact]
    public void Thumbnail_uses_correct_aspect_ratio_contract_and_is_lazy()
    {
        var view = Read("InovaGed.Web/Views/Labels/Designer/Index.cshtml");
        var controller = Read("InovaGed.Web/Controller/LabelDesignerController.cs");
        Assert.Contains("item.WidthMm / item.HeightMm", view);
        Assert.Contains("data-thumbnail-src", view);
        Assert.Contains("IntersectionObserver", view);
        Assert.Contains("renderer.Render(design, preview.Values, false)", controller);
        Assert.DoesNotContain("src=\"@Url.Action(\"Preview\"", view);
    }

    private static int Count(string value, string token) => (value.Length - value.Replace(token, "").Length) / token.Length;
    private static string Read(string path) => File.ReadAllText(GlobalJsonContractTests.Root(path));
}
