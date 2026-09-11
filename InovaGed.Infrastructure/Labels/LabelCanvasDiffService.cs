using System.Text.Json;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasDiffService : ILabelCanvasDiffService
{
    public LabelCanvasDiffResult Compare(string beforeJson, string afterJson)
    {
        using var before = JsonDocument.Parse(beforeJson);
        using var after = JsonDocument.Parse(afterJson);
        var oldElements = Elements(before.RootElement);
        var newElements = Elements(after.RootElement);
        var added = newElements.Keys.Except(oldElements.Keys).Order().Select(id => Describe(newElements[id])).ToArray();
        var removed = oldElements.Keys.Except(newElements.Keys).Order().Select(id => Describe(oldElements[id])).ToArray();
        var changed = oldElements.Keys.Intersect(newElements.Keys).Order().Select(id => Change(id, oldElements[id], newElements[id])).Where(change => change.Changes.Count > 0).ToArray();
        var metadataChanged = !Same(Property(before.RootElement, "canvas"), Property(after.RootElement, "canvas")) ||
                              !Same(Property(before.RootElement, "bindings"), Property(after.RootElement, "bindings"));
        return new(metadataChanged, added, removed, changed);
    }

    private static Dictionary<string, JsonElement> Elements(JsonElement root) =>
        root.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array
            ? elements.EnumerateArray().Where(e => e.TryGetProperty("id", out _)).ToDictionary(e => Text(e, "id"), e => e.Clone(), StringComparer.Ordinal)
            : new(StringComparer.Ordinal);

    private static LabelCanvasElementChange Change(string id, JsonElement before, JsonElement after)
    {
        var changes = new List<string>();
        Add(changes, LabelCanvasChangeKind.Position, before, after, "xMm", "yMm", "rotationDeg");
        Add(changes, LabelCanvasChangeKind.Size, before, after, "widthMm", "heightMm");
        Add(changes, LabelCanvasChangeKind.Text, before, after, "text");
        Add(changes, LabelCanvasChangeKind.Style, before, after, "style");
        Add(changes, LabelCanvasChangeKind.Binding, before, after, "binding");
        Add(changes, LabelCanvasChangeKind.Visibility, before, after, "visible", "visibilityCondition");
        Add(changes, LabelCanvasChangeKind.Lock, before, after, "locked");
        Add(changes, LabelCanvasChangeKind.Group, before, after, "groupId");
        Add(changes, LabelCanvasChangeKind.Type, before, after, "type");
        return new(id, Text(after, "name", id), changes);
    }

    private static void Add(List<string> result, string kind, JsonElement before, JsonElement after, params string[] properties)
    {
        if (properties.Any(property => !Same(Property(before, property), Property(after, property)))) result.Add(kind);
    }

    private static LabelCanvasDiffElement Describe(JsonElement element) => new(Text(element, "id"), Text(element, "name", Text(element, "id")), Text(element, "type"));
    private static string Text(JsonElement element, string property, string fallback = "") => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static JsonElement? Property(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value : null;
    private static bool Same(JsonElement? left, JsonElement? right) => left?.ValueKind == right?.ValueKind && (left is null || JsonElement.DeepEquals(left.Value, right!.Value));
}
