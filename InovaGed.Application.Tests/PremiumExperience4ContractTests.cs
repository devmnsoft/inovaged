using System.Xml.Linq;
using InovaGed.Web.Services;

namespace InovaGed.Application.Tests;

public sealed class PremiumExperience4ContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Illustration_assets_are_safe_local_responsive_svgs()
    {
        string[] required = ["login-document-management", "empty-folder", "empty-search", "empty-notifications", "upload-files", "ocr-processing", "smart-search", "intelligent-chat", "access-denied", "system-error", "module-unavailable"];
        foreach (var name in required)
        {
            var path = Path.Combine(Root, "InovaGed.Web", "wwwroot", "images", "illustrations", name + ".svg");
            Assert.True(File.Exists(path), $"Asset ausente: {name}");
            var document = XDocument.Load(path);
            var root = Assert.IsType<XElement>(document.Root);
            Assert.NotNull(root.Attribute("viewBox"));
            Assert.NotNull(root.Attribute("width"));
            Assert.NotNull(root.Attribute("height"));
            var content = File.ReadAllText(path);
            Assert.DoesNotContain("<script", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("data:image", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Icon_catalog_has_unique_semantic_entries_across_core_categories()
    {
        var icons = new IconCatalog().GetAll();
        Assert.Equal(icons.Count, icons.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var category in new[] { "Navegação", "Documentos", "Upload", "OCR", "Busca", "Chat", "Administração", "Alertas", "Estados", "Ações" })
            Assert.Contains(icons, icon => icon.Category == category);
    }

    [Fact]
    public void Motion_and_feedback_contract_respect_accessibility_limits()
    {
        var tokens = File.ReadAllText(Path.Combine(Root, "InovaGed.Web", "wwwroot", "css", "inovaged.tokens.css"));
        Assert.Contains("--ig-motion-slow:260ms", tokens);
        var components = File.ReadAllText(Path.Combine(Root, "InovaGed.Web", "wwwroot", "css", "inovaged.components.css"));
        Assert.Contains("prefers-reduced-motion:reduce", components);
        var feedback = File.ReadAllText(Path.Combine(Root, "InovaGed.Web", "wwwroot", "js", "inovaged-feedback.js"));
        Assert.Contains("visible < 3", feedback);
        Assert.Contains("mouseenter", feedback);
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
