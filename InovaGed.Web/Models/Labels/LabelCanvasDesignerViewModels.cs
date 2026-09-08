using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Branding;

namespace InovaGed.Web.Models.Labels;

public sealed record LabelCanvasDesignerPageViewModel(LabelCanvasDesignDto Design,IReadOnlyList<LabelCanvasFieldDto> Fields,LabelCanvasValidationResult Validation,string? RenderHtml=null,bool IsNew=false,IReadOnlyList<ResolvedPrintBranding>? BrandingProfiles=null);
public sealed record LabelCanvasVersionsPageViewModel(LabelCanvasDesignDto Design,IReadOnlyList<LabelCanvasVersionDto> Versions);
