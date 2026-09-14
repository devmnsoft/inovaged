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
        var metadataChanges = MetadataChanges(before.RootElement, after.RootElement);
        return new(metadataChanges.Count > 0, added, removed, changed, metadataChanges);
    }

    private static Dictionary<string, JsonElement> Elements(JsonElement root)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array) return result;

        foreach (var element in elements.EnumerateArray())
        {
            var id = Text(element, "id").Trim();
            if (id.Length == 0)
                throw new JsonException("Há um elemento sem identificador interno válido.");
            if (!result.TryAdd(id, element.Clone()))
                throw new JsonException($"Há mais de um elemento com o identificador interno '{id}'.");
        }
        return result;
    }

    private static IReadOnlyList<string> MetadataChanges(JsonElement before, JsonElement after)
    {
        var changes = new List<string>();
        var oldCanvas = Property(before, "canvas");
        var newCanvas = Property(after, "canvas");
        AddMetadata(changes, LabelCanvasMetadataChangeKind.CanvasSize, oldCanvas, newCanvas, "widthMm", "heightMm");
        AddMetadata(changes, LabelCanvasMetadataChangeKind.Paper, oldCanvas, newCanvas, "paper");
        AddMetadata(changes, LabelCanvasMetadataChangeKind.Orientation, oldCanvas, newCanvas, "orientation");
        AddMetadata(changes, LabelCanvasMetadataChangeKind.Grid, oldCanvas, newCanvas, "gridMm");
        AddMetadata(changes, LabelCanvasMetadataChangeKind.SafeMargin, oldCanvas, newCanvas, "safeMarginMm");
        if (!Same(Property(before, "bindings"), Property(after, "bindings"))) changes.Add(LabelCanvasMetadataChangeKind.Bindings);
        return changes;
    }

    private static void AddMetadata(List<string> changes, string kind, JsonElement? before, JsonElement? after, params string[] properties)
    {
        if (properties.Any(property => !Same(Property(before, property), Property(after, property)))) changes.Add(kind);
    }

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
    private static JsonElement? Property(JsonElement? element, string name) => element is null ? null : Property(element.Value, name);

    private static bool Same(JsonElement? left, JsonElement? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return SameElement(left.Value, right.Value);
    }

    private static bool SameElement(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.Object => SameObject(left, right),
            JsonValueKind.Array => SameArray(left, right),
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => SameNumber(left, right),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            _ => false
        };
    }

    private static bool SameObject(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToArray();
        var rightProperties = right.EnumerateObject().ToArray();
        if (leftProperties.Length != rightProperties.Length) return false;
        return leftProperties.All(property =>
            right.TryGetProperty(property.Name, out var rightValue) && SameElement(property.Value, rightValue));
    }

    private static bool SameArray(JsonElement left, JsonElement right)
    {
        if (left.GetArrayLength() != right.GetArrayLength()) return false;
        var rightItems = right.EnumerateArray().GetEnumerator();
        foreach (var leftItem in left.EnumerateArray())
        {
            if (!rightItems.MoveNext() || !SameElement(leftItem, rightItems.Current)) return false;
        }
        return true;
    }

    private static bool SameNumber(JsonElement left, JsonElement right)
    {
        if (left.TryGetDecimal(out var leftDecimal) && right.TryGetDecimal(out var rightDecimal))
            return leftDecimal == rightDecimal;
        if (left.TryGetDouble(out var leftDouble) && right.TryGetDouble(out var rightDouble))
            return leftDouble.Equals(rightDouble);
        return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
    }
}
