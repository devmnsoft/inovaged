using Microsoft.AspNetCore.Razor.TagHelpers;

namespace InovaGed.Web.TagHelpers;

[HtmlTargetElement("atlas-illustration", Attributes = "name")]
public sealed class AtlasIllustrationTagHelper : TagHelper
{
    private static readonly IReadOnlyDictionary<string, string> Assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["login-ged-workspace"] = "login-ged-workspace.svg",
        ["empty-folder"] = "empty-folder-atlas.svg", ["empty-search"] = "empty-search-atlas.svg",
        ["upload-drop"] = "upload-drop-atlas.svg", ["upload-validating"] = "upload-validating-atlas.svg",
        ["upload-processing"] = "upload-processing-atlas.svg", ["upload-success"] = "upload-success-atlas.svg",
        ["upload-error"] = "upload-error-atlas.svg", ["upload-duplicate"] = "upload-duplicate-atlas.svg",
        ["preview-empty"] = "preview-empty-atlas.svg", ["preview-loading"] = "preview-loading-atlas.svg",
        ["preview-unsupported"] = "preview-unsupported-atlas.svg", ["preview-error"] = "preview-error-atlas.svg",
        ["preview-restricted"] = "preview-restricted-atlas.svg", ["favorites-empty"] = "favorites-empty-atlas.svg",
        ["recents-empty"] = "recents-empty-atlas.svg", ["activity-empty"] = "activity-empty-atlas.svg",
        ["notifications-empty"] = "notifications-empty-atlas.svg", ["saved-views-empty"] = "saved-views-empty-atlas.svg",
        ["work-queue-empty"] = "work-queue-empty-atlas.svg", ["assistant-sources"] = "assistant-sources-atlas.svg"
    };
    private static readonly IReadOnlyDictionary<string, (int Width, int Height)> Sizes = new Dictionary<string, (int, int)>
    { ["small"] = (160, 120), ["medium"] = (280, 210), ["large"] = (480, 360), ["hero"] = (640, 480) };

    public string Name { get; set; } = string.Empty;
    public string Size { get; set; } = "medium";
    public string? Alt { get; set; }
    public bool Decorative { get; set; }
    public bool Eager { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!Assets.TryGetValue(Name, out var asset)) throw new InvalidOperationException($"Atlas illustration not registered: {Name}");
        var dimensions = Sizes.TryGetValue(Size, out var configured) ? configured : Sizes["medium"];
        var existing = output.Attributes["class"]?.Value?.ToString();
        output.TagName = "img";
        output.TagMode = TagMode.SelfClosing;
        output.Attributes.SetAttribute("src", $"/images/atlas/{asset}");
        output.Attributes.SetAttribute("class", $"atlas-illustration atlas-illustration--{Size} {existing}".Trim());
        output.Attributes.SetAttribute("width", dimensions.Width);
        output.Attributes.SetAttribute("height", dimensions.Height);
        output.Attributes.SetAttribute("alt", Decorative ? string.Empty : Alt ?? throw new InvalidOperationException("Informative Atlas illustrations require alt text."));
        if (Decorative) output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("loading", Eager ? "eager" : "lazy");
        output.Attributes.SetAttribute("decoding", "async");
    }
}
