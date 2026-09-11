namespace InovaGed.Application.Labels.Canvas;

public interface ILabelCanvasDiffService
{
    LabelCanvasDiffResult Compare(string beforeJson, string afterJson);
}

public sealed record LabelCanvasDiffResult(
    bool TemplateMetadataChanged,
    IReadOnlyList<LabelCanvasDiffElement> Added,
    IReadOnlyList<LabelCanvasDiffElement> Removed,
    IReadOnlyList<LabelCanvasElementChange> Changed);

public sealed record LabelCanvasDiffElement(string ElementId, string Name, string Type);
public sealed record LabelCanvasElementChange(string ElementId, string Name, IReadOnlyList<string> Changes);

public static class LabelCanvasChangeKind
{
    public const string Position = "POSITION";
    public const string Size = "SIZE";
    public const string Text = "TEXT";
    public const string Style = "STYLE";
    public const string Binding = "BINDING";
    public const string Visibility = "VISIBILITY";
    public const string Lock = "LOCK";
    public const string Group = "GROUP";
    public const string Type = "TYPE";
}
