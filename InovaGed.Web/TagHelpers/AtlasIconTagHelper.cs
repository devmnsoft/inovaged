using InovaGed.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Hosting;

namespace InovaGed.Web.TagHelpers;

[HtmlTargetElement("app-icon", Attributes = "name")]
[HtmlTargetElement("atlas-icon", Attributes = "name")]
public sealed class AtlasIconTagHelper(
    IAtlasIconRegistry registry,
    IWebHostEnvironment environment,
    ILogger<AtlasIconTagHelper> logger) : TagHelper
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["archive"] = "physical-archive", ["tag"] = "label",
            ["bookmark"] = "favorite", ["alarm"] = "recent",
            ["plus"] = "document-add", ["plus-lg"] = "document-add", ["eye"] = "preview",
            ["bi-people"] = "users", ["bi-person-lock"] = "restricted-access",
            ["bi-building"] = "physical-archive", ["bi-person-badge"] = "users",
            ["building"] = "physical-archive", ["qr"] = "search", ["scanner"] = "search",
            ["map"] = "location", ["pin"] = "location", ["map-pin"] = "location",
            ["printer"] = "print", ["qr-code"] = "search", ["box"] = "physical-archive",
            ["file-text"] = "document", ["git-branch"] = "history", ["activity"] = "recent",
            ["shield"] = "security", ["key"] = "restricted-access", ["alert-triangle"] = "warning",
            ["panels-top-left"] = "workspace", ["scan-line"] = "activity",
            ["route"] = "timeline", ["refresh-cw"] = "refresh", ["shield-check"] = "permissions"
        };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> WarnedIcons = new(StringComparer.OrdinalIgnoreCase);
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int Size { get; set; } = 20;
    public string? Tone { get; set; }
    public string? Variant { get; set; }
    public string? Title { get; set; }
    public bool Decorative { get; set; }
    public bool Filled { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var requestedName = Name.Trim();
        var resolvedName = Aliases.TryGetValue(requestedName, out var alias) ? alias : requestedName;
        var found = registry.TryGet(resolvedName, out var definition);
        if (!found)
        {
            if (WarnedIcons.TryAdd(requestedName, 0))
                logger.LogWarning("Atlas icon not found: {IconName}", requestedName);
            var fallback = environment.IsDevelopment() ? "missing" : "circle-question";
            registry.TryGet(fallback, out definition!);
            if (environment.IsDevelopment()) output.Attributes.SetAttribute("data-missing-icon", Name);
        }

        var existing = output.Attributes["class"]?.Value?.ToString();
        var classes = new[]
        {
            "atlas-icon", existing, $"atlas-icon--{NormalizeSize(Size)}",
            CssModifier(Tone), CssModifier(Filled ? "filled" : Variant ?? definition.Variant)
        }.Where(value => !string.IsNullOrWhiteSpace(value));

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", string.Join(' ', classes));
        output.Attributes.SetAttribute("width", NormalizeSize(Size));
        output.Attributes.SetAttribute("height", NormalizeSize(Size));
        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
        output.Attributes.SetAttribute("focusable", "false");

        var accessibleLabel = Label ?? Title;
        if (Decorative || string.IsNullOrWhiteSpace(accessibleLabel))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", accessibleLabel);
        }

        if (!string.IsNullOrWhiteSpace(Title))
        {
            output.Attributes.SetAttribute("title", Title);
        }

        var titleMarkup = string.IsNullOrWhiteSpace(Title) ? string.Empty : $"<title>{System.Net.WebUtility.HtmlEncode(Title)}</title>";
        output.Content.SetHtmlContent($"{titleMarkup}<use href=\"#{definition.SymbolId}\"></use>");
    }

    private static int NormalizeSize(int size) => size is >= 12 and <= 64 ? size : 20;
    private static string? CssModifier(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : $"atlas-icon--{new string(value.Where(character => char.IsLetterOrDigit(character) || character == '-').ToArray()).ToLowerInvariant()}";
}
