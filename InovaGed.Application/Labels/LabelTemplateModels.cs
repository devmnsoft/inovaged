using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Labels;

public sealed record LabelTemplateListItem(Guid Id,string Code,string Name,string? Description,string PrintMode,string SubjectType,string ViewName,int Version,bool IsSystemTemplate,bool IsCustomTemplate,bool IsActive,bool IsDefault,bool SupportsBatch,bool AllowsManualFields,int DisplayOrder);
public sealed class LabelTemplateFieldItem { public Guid? Id {get;set;} public string FieldKey {get;set;}=""; public string FieldLabel {get;set;}=""; public string FieldType {get;set;}="TEXT"; public bool IsVisible {get;set;}=true; public bool IsRequired {get;set;} public bool IsEditable {get;set;}=true; public string? DefaultValue {get;set;} public int DisplayOrder {get;set;} public string? CssClass {get;set;} }
public sealed class LabelTemplateConfigDetails { public string? HeaderText {get;set;} public string? LogoSvg {get;set;} public string? PrimaryColor {get;set;}="#008a9a"; public string? SecondaryColor {get;set;}="#6fc7c8"; public string? BorderColor {get;set;}="#111111"; public string? TextColor {get;set;}="#111111"; public string? AccentColor {get;set;}="#d60000"; public string PageSize {get;set;}="A4"; public decimal? LabelWidthMm {get;set;}=190; public decimal? LabelHeightMm {get;set;}=130; public int LabelsPerPage {get;set;}=1; public string Orientation {get;set;}="PORTRAIT"; public string? CustomCss {get;set;} public decimal MarginTopMm {get;set;} public decimal MarginRightMm {get;set;} public decimal MarginBottomMm {get;set;} public decimal MarginLeftMm {get;set;} }
public sealed record LabelTemplateDetails(LabelTemplateListItem Template,LabelTemplateConfigDetails Config,IReadOnlyList<LabelTemplateFieldItem> Fields);
public sealed class LabelTemplateEditCommand {
 [Required,StringLength(160)] public string Name {get;set;}=""; [StringLength(1000)] public string? Description {get;set;}
 [Required,StringLength(80)] public string Code {get;set;}=""; [Required] public string SubjectType {get;set;}="BOX";
 public bool IsActive {get;set;}=true; public bool SupportsBatch {get;set;}=true; public bool AllowsManualFields {get;set;}
 public LabelTemplateConfigDetails Config {get;set;}=new();
 public List<LabelTemplateFieldItem> Fields {get;set;}=[];
}
public sealed record LabelTemplateVersionItem(Guid Id,int VersionNumber,Guid? PublishedBy,DateTimeOffset PublishedAt,string? ChangeNotes);
public sealed record LabelTemplateVersionDetails(Guid Id,Guid TemplateId,int VersionNumber,string SnapshotJson,Guid? PublishedBy,DateTimeOffset PublishedAt,string? ChangeNotes);
public sealed record LabelRenderDefinition(LabelTemplateDetails Template,IReadOnlyDictionary<string,string?> Values);
