using System.Text.Json;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;

namespace InovaGed.Application.Tests;

public sealed class LabelCanvasSemanticDiffRc341Tests
{
    private readonly LabelCanvasDiffService service = new();

    [Fact] public void json_same_simple_values() => AssertNoChanges(Element("\"text\":\"Arquivo\",\"visible\":true"), Element("\"text\":\"Arquivo\",\"visible\":true"));
    [Fact] public void json_same_object_different_property_order() => AssertNoChanges(Element("\"style\":{\"fontSizePt\":10,\"color\":\"#000000\"}"), Element("\"style\":{\"color\":\"#000000\",\"fontSizePt\":10}"));
    [Fact] public void json_same_nested_object_different_property_order() => AssertNoChanges(Element("\"style\":{\"border\":{\"width\":1,\"color\":\"black\"},\"align\":\"left\"}"), Element("\"style\":{\"align\":\"left\",\"border\":{\"color\":\"black\",\"width\":1}}"));
    [Fact] public void json_same_number_10_and_10_0() => AssertNoChanges(Element("\"style\":{\"fontSizePt\":10}"), Element("\"style\":{\"fontSizePt\":10.0}"));
    [Fact] public void json_different_numbers() => AssertChange(Element("\"style\":{\"fontSizePt\":10}"), Element("\"style\":{\"fontSizePt\":11}"), LabelCanvasChangeKind.Style);
    [Fact] public void json_same_arrays() => AssertNoChanges(Element("\"style\":{\"dash\":[1,2,3]}"), Element("\"style\":{\"dash\":[1,2,3]}"));
    [Fact] public void json_different_array_order() => AssertChange(Element("\"style\":{\"dash\":[1,2]}"), Element("\"style\":{\"dash\":[2,1]}"), LabelCanvasChangeKind.Style);
    [Fact] public void json_same_null() => AssertNoChanges(Element("\"binding\":null"), Element("\"binding\":null"));
    [Fact] public void json_null_vs_missing_is_different() => AssertChange(Element("\"binding\":null"), Element(), LabelCanvasChangeKind.Binding);
    [Fact] public void json_same_boolean() => AssertNoChanges(Element("\"visible\":true"), Element("\"visible\":true"));
    [Fact] public void semantic_diff_detects_binding_change() => AssertChange(Element("\"binding\":{\"field\":\"classification\"}"), Element("\"binding\":{\"field\":\"controlNumber\"}"), LabelCanvasChangeKind.Binding);
    [Fact] public void semantic_diff_detects_position_change() => AssertChange(Element("\"xMm\":10"), Element("\"xMm\":11"), LabelCanvasChangeKind.Position);
    [Fact] public void semantic_diff_detects_size_change() => AssertChange(Element("\"widthMm\":30"), Element("\"widthMm\":40"), LabelCanvasChangeKind.Size);
    [Fact] public void semantic_diff_detects_added_element() => Assert.Single(service.Compare(Document(), Document(Element())).Added);
    [Fact] public void semantic_diff_detects_removed_element() => Assert.Single(service.Compare(Document(Element()), Document()).Removed);

    [Fact]
    public void style_and_binding_property_order_do_not_change_diff()
    {
        var before = Element("\"style\":{\"fontSizePt\":10,\"fontWeight\":\"700\",\"color\":\"#000000\"},\"binding\":{\"field\":\"classification\",\"fallback\":\"-\"}");
        var after = Element("\"binding\":{\"fallback\":\"-\",\"field\":\"classification\"},\"style\":{\"color\":\"#000000\",\"fontWeight\":\"700\",\"fontSizePt\":10}");
        AssertNoChanges(before, after);
    }

    [Fact]
    public void duplicate_element_id_is_rejected()
    {
        var exception = Assert.Throws<JsonException>(() => service.Compare(Document(), Document(Element(), Element())));
        Assert.Contains("mais de um elemento", exception.Message);
    }

    [Fact]
    public void element_without_id_is_rejected()
    {
        var exception = Assert.Throws<JsonException>(() => service.Compare(Document(), "{\"elements\":[{\"name\":\"Inválido\"}]}"));
        Assert.Equal("Há um elemento sem identificador interno válido.", exception.Message);
    }

    private void AssertNoChanges(string beforeElement, string afterElement)
    {
        var result = service.Compare(Document(beforeElement), Document(afterElement));
        Assert.False(result.TemplateMetadataChanged);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Changed);
    }

    private void AssertChange(string beforeElement, string afterElement, string expected)
    {
        var change = Assert.Single(service.Compare(Document(beforeElement), Document(afterElement)).Changed);
        Assert.Contains(expected, change.Changes);
    }

    private static string Element(string? properties = null) => $"{{\"id\":\"element-1\",\"name\":\"Classificação documental\",\"type\":\"field\"{(properties is null ? "" : "," + properties)}}}";
    private static string Document(params string[] elements) => $"{{\"canvas\":{{\"widthMm\":100,\"heightMm\":70}},\"bindings\":{{\"subjectType\":\"Document\"}},\"elements\":[{string.Join(',', elements)}]}}";
}
