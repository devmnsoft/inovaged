using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Web.Models.Labels;

public sealed record LabelCanvasDesignerPageViewModel(LabelCanvasDesignDto Design,IReadOnlyList<LabelCanvasFieldDto> Fields,LabelCanvasValidationResult Validation,string? RenderHtml=null,bool IsNew=false);
public sealed record LabelCanvasVersionsPageViewModel(LabelCanvasDesignDto Design,IReadOnlyList<LabelCanvasVersionDto> Versions);
