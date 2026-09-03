using InovaGed.Application.Labels;
using InovaGed.Web.Models.Branding;
namespace InovaGed.Web.Models.Labels;
public sealed class LocDeskLabelRenderModel
{
    public LocDeskLabelInputModel Label { get; init; } = new();
    public string QrSvg { get; init; } = "";
    public bool PrintRegistered { get; init; }
    public LabelTemplateDetails? Template { get; init; }
    public IReadOnlyList<LocDeskLabelInputModel> Labels { get; init; } = [];
    public PrintLogoViewModel PrintLogo { get; init; } = PrintLogoViewModel.Empty;
    public string? PrintLogoWarning { get; init; }
    public IReadOnlyList<LocDeskLabelRenderModel> RenderLabels { get; init; } = [];
}
