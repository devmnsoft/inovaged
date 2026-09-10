using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Branding;

namespace InovaGed.Web.Models.Labels;

public sealed record LabelCanvasDesignerPageViewModel(LabelCanvasDesignDto Design,IReadOnlyList<LabelCanvasFieldDto> Fields,LabelCanvasValidationResult Validation,string? RenderHtml=null,bool IsNew=false,IReadOnlyList<ResolvedPrintBranding>? BrandingProfiles=null);
public sealed record LabelCanvasVersionsPageViewModel(LabelCanvasDesignDto Design,IReadOnlyList<LabelCanvasVersionDto> Versions);
public sealed class LabelCanvasNewModelInput
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(120)] public string Name { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Required] public string SubjectType { get; set; } = "Box";
    [System.ComponentModel.DataAnnotations.Required] public string StarterKind { get; set; } = "INSTITUTIONAL";
    [System.ComponentModel.DataAnnotations.Range(20,500)] public decimal WidthMm { get; set; } = 100;
    [System.ComponentModel.DataAnnotations.Range(20,500)] public decimal HeightMm { get; set; } = 70;
    public Guid? BrandingProfileId { get; set; }
    public string PaperKind { get; set; } = "A4";
    public IReadOnlyList<ResolvedPrintBranding> BrandingProfiles { get; set; } = Array.Empty<ResolvedPrintBranding>();
}
