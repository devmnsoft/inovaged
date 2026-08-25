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

public sealed record AtlasKpiCardVm(string Label, string Value, string Hint, string Icon = "information", string Tone = "neutral");
public sealed record AtlasActionCardVm(string Title, string Description, string Url, string Icon = "arrow-right", string LinkLabel = "Acessar");
public sealed record AtlasStatusBadgeVm(string Label, string Tone = "neutral");
public sealed record AtlasAlertPanelVm(string Title, string Message, string Tone = "information", string Icon = "information", AtlasPageAction? Action = null);
public sealed record AtlasFormSectionVm(string Title, string Description, string? Icon = null);
public sealed record AtlasPageToolbarVm(string Title, string? Description = null, IReadOnlyList<AtlasPageAction>? Actions = null);
