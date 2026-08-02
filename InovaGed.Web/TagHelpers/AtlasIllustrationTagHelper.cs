using InovaGed.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace InovaGed.Web.TagHelpers;

[HtmlTargetElement("atlas-illustration", Attributes = "name")]
public sealed class AtlasIllustrationTagHelper(IAtlasIllustrationRegistry registry) : TagHelper
{
    private static readonly IReadOnlyDictionary<string, int> DisplayWidths = new Dictionary<string, int>
    {
        ["small"] = 160, ["medium"] = 280, ["large"] = 480, ["hero"] = 640
    };

    public string Name { get; set; } = string.Empty;
    public string Size { get; set; } = "medium";
    public string? Alt { get; set; }
    public bool? Decorative { get; set; }
    public string Loading { get; set; } = "lazy";
    public bool Eager { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!registry.TryGet(Name, out var definition))
        {
            throw new InvalidOperationException($"Atlas illustration not registered: {Name}");
        }

        var decorative = Decorative ?? definition.DecorativeByDefault;
        var width = DisplayWidths.TryGetValue(Size, out var configuredWidth) ? configuredWidth : DisplayWidths["medium"];
        var height = (int)Math.Round(width * (definition.Height / (double)definition.Width));
        var existing = output.Attributes["class"]?.Value?.ToString();
        var loading = Eager ? "eager" : Loading.Equals("eager", StringComparison.OrdinalIgnoreCase) ? "eager" : "lazy";

        output.TagName = "img";
        output.TagMode = TagMode.SelfClosing;
        output.Attributes.SetAttribute("src", definition.Path);
        output.Attributes.SetAttribute("class", $"atlas-illustration atlas-illustration--{Size} {existing}".Trim());
        output.Attributes.SetAttribute("width", width);
        output.Attributes.SetAttribute("height", height);
        output.Attributes.SetAttribute("alt", decorative ? string.Empty : Alt ?? throw new InvalidOperationException("Informative Atlas illustrations require alt text."));
        if (decorative) output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("loading", loading);
        output.Attributes.SetAttribute("decoding", "async");
    }
}
