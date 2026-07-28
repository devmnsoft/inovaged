using System.Text.Encodings.Web;
using InovaGed.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace InovaGed.Web.TagHelpers;

[HtmlTargetElement("app-icon", Attributes = "name")]
public sealed class AppIconTagHelper(IIconCatalog catalog) : TagHelper
{
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "svg";
        output.Attributes.SetAttribute("class", "app-icon");
        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
        output.Attributes.SetAttribute("fill", "none");
        output.Attributes.SetAttribute("stroke", "currentColor");
        output.Attributes.SetAttribute("stroke-width", "1.8");
        output.Attributes.SetAttribute("stroke-linecap", "round");
        output.Attributes.SetAttribute("stroke-linejoin", "round");
        if (string.IsNullOrWhiteSpace(Label)) output.Attributes.SetAttribute("aria-hidden", "true");
        else { output.Attributes.SetAttribute("role", "img"); output.Attributes.SetAttribute("aria-label", Label); }
        var path = catalog.TryGetPath(Name, out var value) ? value : "M4 4h16v16H4z";
        output.Content.SetHtmlContent($"<path d=\"{HtmlEncoder.Default.Encode(path)}\"></path>");
    }
}
