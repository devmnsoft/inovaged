namespace InovaGed.Web.Models.Labels;
public sealed class LabelPrintPreviewViewModel { public required LabelPrintWizardInputModel Input {get;init;} public required LabelTemplateOptionViewModel Template {get;init;} public object? Subject {get;init;} public string? QrSvg {get;init;} }
