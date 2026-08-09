namespace InovaGed.Web.Models.Atlas;

public sealed record AtlasPageAction(string Label, string Url, string Icon = "arrow-right", bool Primary = false);

public sealed record AtlasPageHeaderVm(
    string Title,
    string Subtitle,
    string Icon,
    string Eyebrow,
    IReadOnlyList<AtlasPageAction>? Actions = null,
    IReadOnlyList<(string Label, string? Url)>? Breadcrumbs = null);

public sealed record AtlasMetricVm(
    string Label,
    string Value,
    string Hint,
    string Tone = "neutral",
    string Icon = "information",
    string? Url = null);

public sealed record AtlasDataStateVm(
    string Kind,
    string Title,
    string Message,
    string Icon = "information",
    AtlasPageAction? Action = null);
