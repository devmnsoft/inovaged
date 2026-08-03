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

    [Fact]
    public void Atlas_31_required_semantic_icons_are_registered_and_have_distinct_symbols()
    {
        string[] required = ["dashboard", "workspace", "documents", "document-add", "document-search", "document-version", "document-history", "document-move", "document-link", "metadata", "classification", "retention", "destination", "smart-search", "filter", "sort", "saved-view", "upload-cloud", "upload-pause", "upload-resume", "upload-retry", "folder-add", "folder-move", "folder-favorite", "protocol", "protocol-add", "workflow", "loan", "return", "overdue", "signature", "certificate", "certificate-validation", "audit", "report", "health", "database", "roles", "users", "permissions", "assistant", "table", "list", "cards", "columns", "zoom-in", "zoom-out", "fullscreen", "copy", "print", "download", "arrow-left", "arrow-right", "warning", "error", "success", "information", "restricted-access", "dicom", "pacs", "ocr", "physical-archive", "location", "box", "label"];
        var registry = new AtlasIconRegistry();
        var sprite = XDocument.Load(Path.Combine(Root, "InovaGed.Web", "Views", "Shared", "Icons", "_AtlasIconSprite.cshtml"));
        var symbols = sprite.Descendants().Where(element => element.Name.LocalName == "symbol").ToDictionary(element => element.Attribute("id")?.Value ?? "", StringComparer.OrdinalIgnoreCase);

        foreach (var name in required)
        {
            Assert.True(registry.TryGet(name, out var definition), $"Icone não registrado: {name}");
            Assert.True(symbols.ContainsKey(definition.SymbolId), $"Symbol ausente: {definition.SymbolId}");
            Assert.Equal("0 0 24 24", symbols[definition.SymbolId].Attribute("viewBox")?.Value);
        }

        var geometries = required.Select(name => { registry.TryGet(name, out var definition); return string.Concat(symbols[definition.SymbolId].Nodes().Select(node => node.ToString(SaveOptions.DisableFormatting))); }).ToArray();
        Assert.Equal(geometries.Length, geometries.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Atlas_31_feedback_and_login_do_not_regress_to_bootstrap_icons_or_native_dialogs()
    {
        string[] files = ["wwwroot/js/inovaged-feedback.js", "wwwroot/js/login.js", "Views/Account/Login.cshtml", "Views/Shared/Feedback/_ConfirmDialog.cshtml"];
        foreach (var relative in files)
        {
            var content = File.ReadAllText(Path.Combine(Root, "InovaGed.Web", relative));
            Assert.DoesNotContain("bi-", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"\b(alert|confirm|prompt)\s*\(", content);
        }
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
