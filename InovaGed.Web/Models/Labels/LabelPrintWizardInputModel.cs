using System.ComponentModel.DataAnnotations;
namespace InovaGed.Web.Models.Labels;
public sealed class LabelPrintWizardInputModel : IValidatableObject
{
    [Required] public string SubjectType { get; set; }=""; public Guid? SubjectId { get; set; }
    [Required] public string PrintMode { get; set; }=LabelPrintMode.Factory; [Required] public string TemplateCode { get; set; }="";
    [Range(1,500)] public int Copies { get; set; }=1; [StringLength(500)] public string? ReprintReason { get; set; }
    public Guid? PrintProfileId { get; set; }
    public string LogoSelection { get; set; } = "TEMPLATE_DEFAULT";
    public Guid? SelectedLogoAssetId { get; set; }
    [Range(10,90)] public decimal? SelectedLogoWidthMm { get; set; }
    [Range(5,60)] public decimal? SelectedLogoHeightMm { get; set; }
    public bool PreserveLogoAspectRatio { get; set; } = true;
    [RegularExpression("CONTAIN|COVER|FILL")] public string LogoFitMode { get; set; } = "CONTAIN";
    public Guid? PrintBrandingProfileId { get; set; }
    public LocDeskLabelInputModel CustomFields { get; set; }=new();
    public IEnumerable<ValidationResult> Validate(ValidationContext c)
    {
        if (string.IsNullOrWhiteSpace(TemplateCode)) yield return new("Selecione um modelo de etiqueta.", [nameof(TemplateCode)]);
        if (!LabelPrintMode.IsValid(PrintMode)) yield return new("Selecione um modo de impressão.", [nameof(PrintMode)]);
        if (!LabelSubjectType.IsValid(SubjectType)) yield return new("Selecione um tipo de origem válido.", [nameof(SubjectType)]);
        if (SubjectType != LabelSubjectType.Manual && SubjectId is null) yield return new("Selecione uma caixa, documento ou lote antes de imprimir.", [nameof(SubjectId)]);
        if (Copies <= 0) yield return new("A quantidade de cópias deve ser maior que zero.", [nameof(Copies)]);
    }
}
public sealed class LabelBatchPrintInputModel { public string PrintMode {get;set;}=LabelPrintMode.Factory; public string TemplateCode {get;set;}=""; public string SubjectType {get;set;}=""; public List<Guid> SubjectIds {get;set;}=[]; public string? ReprintReason {get;set;} [Range(1,500)] public int Copies {get;set;}=1; }
