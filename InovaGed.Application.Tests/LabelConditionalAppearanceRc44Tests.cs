using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class LabelConditionalAppearanceRc44Tests
{
    private static readonly LabelCanvasRenderService Renderer = new(NullLogger<LabelCanvasRenderService>.Instance);

    [Fact]
    public void Render_applies_whitelisted_conditional_appearance_without_exposing_expressions()
    {
        var design = Design("""
        {"schemaVersion":2,"canvas":{"widthMm":100,"heightMm":70},"elements":[{"id":"priority","type":"field","name":"Prioridade","xMm":3,"yMm":3,"widthMm":40,"heightMm":10,"zIndex":1,"style":{},"binding":{"field":"priority"},"conditionalAppearance":{"enabled":true,"operator":"EQUALS","field":"priority","value":"Urgente","fontWeight":"700","color":"#7a271a","backgroundColor":"#fee4e2","border":"1px solid #111111"}}]}
        """);

        var rendered = Renderer.Render(design, new Dictionary<string, object?> { ["priority"] = "Urgente" });

        Assert.Contains("font-weight:700", rendered.Html);
        Assert.Contains("background:#fee4e2", rendered.Html);
        Assert.DoesNotContain(rendered.Validation.Issues, issue => issue.Code.StartsWith("UNSAFE_CONDITIONAL", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_rejects_arbitrary_conditional_style()
    {
        var json = """
        {"schemaVersion":2,"canvas":{"widthMm":100,"heightMm":70},"elements":[{"id":"priority","type":"text","name":"Prioridade","xMm":3,"yMm":3,"widthMm":40,"heightMm":10,"zIndex":1,"text":"Urgente","style":{},"conditionalAppearance":{"enabled":true,"operator":"EQUALS","field":"priority","value":"Urgente","backgroundColor":"url(javascript:alert(1))","border":"5px dotted red"}}]}
        """;

        Assert.Contains(Renderer.Validate(json).Issues, issue => issue.Code == "UNSAFE_CONDITIONAL_STYLE" && issue.Severity == "ERROR");
    }

    private static LabelCanvasDesignDto Design(string json) => new()
    {
        TemplateKey = "RC44", TemplateName = "RC44", WidthMm = 100, HeightMm = 70, DesignJson = json
    };
}
