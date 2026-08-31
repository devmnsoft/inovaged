namespace InovaGed.Web.Models.Labels;

public sealed record LabelStudioTemplate(
    string Code, string Name, string Description, string SubjectType, string Mode,
    string ViewName, string Version, string Dimensions, bool SupportsBatch,
    bool AllowsManualFields, bool IsSystem, bool IsDefault, string ThumbClass,
    IReadOnlyList<string> Fields);

public sealed record LabelStudioPreviewViewModel(LabelStudioTemplate Template, LocDeskLabelInputModel Sample, bool PrintOnly);
