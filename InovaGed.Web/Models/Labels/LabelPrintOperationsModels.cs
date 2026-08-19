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
    [Range(50,150)] public decimal ScalePercent { get; set; }=100;
    [Range(10,210)] public decimal? LabelWidthMm { get; set; }=95;
    [Range(10,297)] public decimal? LabelHeightMm { get; set; }=55;
    [Range(0,50)] public decimal GapXMm { get; set; }=4;
    [Range(0,50)] public decimal GapYMm { get; set; }=4;
    [Range(1,100)] public int LabelsPerPage { get; set; }=2;
}
