using InovaGed.Application.Labels;
namespace InovaGed.Web.Models.Labels;
public sealed class LocDeskLabelRenderModel
{
    public required LocDeskLabelInputModel Label { get; init; }
    public required string QrSvg { get; init; }
    public bool PrintRegistered { get; init; }
    public LabelTemplateDetails? Template { get; init; }
    public IReadOnlyList<LocDeskLabelInputModel> Labels { get; init; } = [];
}
