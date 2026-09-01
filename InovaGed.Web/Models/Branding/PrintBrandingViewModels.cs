using System.ComponentModel.DataAnnotations;
using InovaGed.Application.Branding;

namespace InovaGed.Web.Models.Branding;

public class PrintBrandingProfileInput
{
    public Guid Id { get; set; }
    [Required, StringLength(80)] public string ProfileCode { get; set; } = "";
    [Required, StringLength(200)] public string ProfileName { get; set; } = "";
    public string? ClientName { get; set; } public string? ContractName { get; set; } public string? OrganizationName { get; set; }
    public Guid? PrimaryLogoAssetId { get; set; } public Guid? SecondaryLogoAssetId { get; set; }
    public string? HeaderTitle { get; set; } public string? HeaderSubtitle { get; set; } public string? HeaderExtraLine { get; set; }
    public string? FooterText { get; set; } public string? FooterExtraLine { get; set; }
    public bool ShowGeneratedAt { get; set; }=true; public bool ShowPageNumber { get; set; }=true; public bool ShowProtocolInfo { get; set; }
    public string LogoPosition { get; set; }="TOP_LEFT"; public string SecondaryLogoPosition { get; set; }="TOP_RIGHT";
    [Range(5,100)] public decimal PrimaryLogoWidthMm { get; set; }=38; [Range(5,100)] public decimal SecondaryLogoWidthMm { get; set; }=28;
    public string PaperSize { get; set; }="A4"; public string Orientation { get; set; }="PORTRAIT";
    [Range(0,50)] public decimal MarginTopMm { get; set; }=10; [Range(0,50)] public decimal MarginRightMm { get; set; }=10; [Range(0,50)] public decimal MarginBottomMm { get; set; }=10; [Range(0,50)] public decimal MarginLeftMm { get; set; }=10;
    public bool IsDefault { get; set; }
}
public sealed class PrintBrandingProfileVm : PrintBrandingProfileInput { public string Status {get;set;}="ACTIVE"; public string? PrimaryLogoName {get;set;} public string? SecondaryLogoName {get;set;} public string? PrimaryLogoUrl => PrimaryLogoAssetId is null?null:$"/Administration/BrandAssets/{PrimaryLogoAssetId}/File"; public string? SecondaryLogoUrl => SecondaryLogoAssetId is null?null:$"/Administration/BrandAssets/{SecondaryLogoAssetId}/File"; }
public sealed class PrintBrandingDashboardVm { public IReadOnlyList<PrintBrandingProfileVm> Profiles {get;init;}=[]; public IReadOnlyList<BrandAssetVm> Assets {get;init;}=[]; public int BindingCount {get;init;} public bool SchemaReady {get;init;} public PrintBrandingProfileVm? DefaultProfile => Profiles.FirstOrDefault(x=>x.IsDefault); }
public sealed class PrintBrandingBindingInput { [Required] public string Context {get;set;}=""; [Required] public string BindingKey {get;set;}=""; public Guid? ProfileId {get;set;} public bool Enabled {get;set;}=true; }
public sealed class PrintBrandingBindingVm { public string Context {get;set;}=""; public string BindingKey {get;set;}=""; public string Description {get;set;}=""; public Guid? ProfileId {get;set;} public string? ProfileName {get;set;} public string? LogoUrl {get;set;} public bool Enabled {get;set;} }
public sealed class PrintBrandingPreviewVm { public ResolvedPrintBranding Branding {get;init;}=new(); public string? PrimaryLogoUrl => Branding.PrimaryLogoAssetId is null?null:$"/Administration/BrandAssets/{Branding.PrimaryLogoAssetId}/File"; public string? SecondaryLogoUrl => Branding.SecondaryLogoAssetId is null?null:$"/Administration/BrandAssets/{Branding.SecondaryLogoAssetId}/File"; }
