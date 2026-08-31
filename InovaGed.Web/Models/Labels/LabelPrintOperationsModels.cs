using System.ComponentModel.DataAnnotations;
namespace InovaGed.Web.Models.Labels;

public sealed class CreatePrintJobInput
{
    [Required] public string SubjectType { get; set; }="BOX";
    [Required] public Guid? SubjectId { get; set; }
    [Required] public string PrintMode { get; set; }="FACTORY";
    [Required] public string TemplateCode { get; set; }="";
    [Range(1,500)] public int Copies { get; set; }=1;
    [StringLength(500)] public string? ReprintReason { get; set; }
}
public sealed class CreateBatchPrintJobInput
{
    [Required] public string SubjectType { get; set; }="BOX";
    [Required] public string PrintMode { get; set; }="FACTORY";
    [Required] public string TemplateCode { get; set; }="";
    [Range(1,500)] public int Copies { get; set; }=1;
    public List<Guid> SubjectIds { get; set; }=[];
    [StringLength(500)] public string? ReprintReason { get; set; }
}
public sealed class LabelCalibrationInput
{
    [Required] public string TemplateCode { get; set; }="FACTORY_BOX_V1";
    [StringLength(200)] public string? PrinterName { get; set; }
    [Range(0,100)] public decimal MarginTopMm { get; set; }
    [Range(0,100)] public decimal MarginLeftMm { get; set; }
    [Range(0,100)] public decimal MarginRightMm { get; set; }
    [Range(0,100)] public decimal MarginBottomMm { get; set; }
    [Range(50,150)] public decimal ScalePercent { get; set; }=100;
    [Range(-100,100)] public decimal HorizontalOffsetMm { get; set; }
    [Range(-100,100)] public decimal VerticalOffsetMm { get; set; }
    [Required, StringLength(40)] public string PaperSize { get; set; }="A4";
    public bool IsDefault { get; set; }
    [Range(10,210)] public decimal? LabelWidthMm { get; set; }=95;
    [Range(10,297)] public decimal? LabelHeightMm { get; set; }=55;
    [Range(0,50)] public decimal GapXMm { get; set; }=4;
    [Range(0,50)] public decimal GapYMm { get; set; }=4;
    [Range(1,100)] public int LabelsPerPage { get; set; }=2;
}

public sealed class LabelPrintProfileInput
{
    public Guid? Id { get; set; }
    [Required, StringLength(120)] public string ProfileName { get; set; } = "";
    [StringLength(200)] public string? PrinterName { get; set; }
    [Required, StringLength(40)] public string PaperSize { get; set; } = "A4";
    [Required, RegularExpression("PORTRAIT|LANDSCAPE")] public string Orientation { get; set; } = "PORTRAIT";
    [Range(0, 30)] public decimal MarginTopMm { get; set; }
    [Range(0, 30)] public decimal MarginLeftMm { get; set; }
    [Range(-20, 20)] public decimal OffsetXMm { get; set; }
    [Range(-20, 20)] public decimal OffsetYMm { get; set; }
    [Range(80, 120)] public decimal ScalePercent { get; set; } = 100;
    [Range(0, 30)] public decimal LabelGapXMm { get; set; }
    [Range(0, 30)] public decimal LabelGapYMm { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class LabelCalibrationPageViewModel
{
    public IReadOnlyList<LabelPrintProfileInput> Profiles { get; init; } = [];
    public LabelPrintProfileInput Form { get; init; } = new();
    public int ValidatedTemplates { get; init; }
    public int VisualAlerts { get; init; }
}

public sealed class LabelSheetInput
{
    [Required] public string TemplateCode { get; set; } = "FACTORY_BOX_V1";
    public Guid? ProfileId { get; set; }
    [Range(1, 100)] public int Quantity { get; set; } = 8;
    [Range(20, 200)] public decimal LabelWidthMm { get; set; } = 95;
    [Range(20, 280)] public decimal LabelHeightMm { get; set; } = 55;
}

public sealed class LabelQualityRow
{
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public IReadOnlyList<string> Checks { get; init; } = [];
    public IReadOnlyList<string> Alerts { get; init; } = [];
    public bool Approved => Alerts.Count == 0;
}
