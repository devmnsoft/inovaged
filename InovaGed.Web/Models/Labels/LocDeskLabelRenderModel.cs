using InovaGed.Application.Labels;
using InovaGed.Web.Models.Branding;
namespace InovaGed.Web.Models.Labels;
public sealed class LocDeskLabelRenderModel
{
    public required LocDeskLabelInputModel Label { get; init; }
    public required string QrSvg { get; init; }
    public bool PrintRegistered { get; init; }
    public LabelTemplateDetails? Template { get; init; }
    public IReadOnlyList<LocDeskLabelInputModel> Labels { get; init; } = [];
    public ResolvedPrintLogo? PrintLogo { get; init; }
    public string? PrintLogoWarning { get; init; }
}
