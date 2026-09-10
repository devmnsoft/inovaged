namespace InovaGed.Application.Tests;

public sealed class LabelDesignerIndexContractTests
{
    [Fact]
    public void Index_uses_the_gallery_view_model_end_to_end()
    {
        var root = FindRoot();
        var controller = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Controller", "LabelDesignerController.cs"));
        var view = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Views", "Labels", "Designer", "Index.cshtml"));

        Assert.Contains("return View(\"~/Views/Labels/Designer/Index.cshtml\", vm);", controller);
        Assert.Contains("new List<LabelTemplateListItemViewModel>()", controller);
        Assert.Contains("@model IReadOnlyList<LabelTemplateListItemViewModel>", view);
        Assert.DoesNotContain("LabelCanvasDesignDto", view);
    }

    [Fact]
    public void Index_mapping_preserves_design_metadata_and_avoids_per_item_branding_calls()
    {
        var controller = File.ReadAllText(Path.Combine(FindRoot(), "InovaGed.Web", "Controller", "LabelDesignerController.cs"));

        Assert.Contains("IsSystem = d.IsSystemTemplate", controller);
        Assert.Contains("IsLegacy = isLegacy", controller);
        Assert.Contains("CanEdit = d.CanEdit", controller);
        Assert.Contains("UpdatedAt = d.UpdatedAt ?? d.CreatedAt", controller);
        Assert.Contains("Purpose = FriendlyPurpose(d.SubjectType)", controller);
        Assert.Contains("GroupBy(p => p.ProfileId!.Value)", controller);
        Assert.DoesNotContain("brandingResolver.ResolveAsync", IndexBody(controller));
    }

    private static string IndexBody(string controller)
    {
        var start = controller.IndexOf("public async Task<IActionResult> Index", StringComparison.Ordinal);
        var end = controller.IndexOf("private static string? FirstNonEmpty", start, StringComparison.Ordinal);
        return controller[start..end];
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Raiz da solução não encontrada.");
    }
}
