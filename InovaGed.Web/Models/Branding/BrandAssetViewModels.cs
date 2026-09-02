using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Branding;

public sealed class BrandAssetUploadInput
{
    [Required, StringLength(160)] public string BrandName { get; set; } = "";
    [Required, StringLength(160)] public string AssetName { get; set; } = "";
    [Required] public IFormFile? File { get; set; }
    public bool IsDefault { get; set; }
    [Range(10, 90)] public decimal DefaultWidthMm { get; set; } = 38;
    [Range(5, 60)] public decimal? DefaultHeightMm { get; set; }
    public bool PreserveAspectRatio { get; set; } = true;
    [RegularExpression("CONTAIN|COVER|FILL")] public string FitMode { get; set; } = "CONTAIN";
    [StringLength(500)] public string? Notes { get; set; }
}

public sealed class BrandAssetVm
{
    public Guid Id { get; set; }
    public string BrandName { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string FileExtension { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string StorageRelativePath { get; set; } = "";
    public bool IsDefault { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset CreatedAt { get; set; }
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public decimal DefaultWidthMm { get; set; } = 38;
    public decimal? DefaultHeightMm { get; set; }
    public bool PreserveAspectRatio { get; set; } = true;
    public string FitMode { get; set; } = "CONTAIN";
    public string DefaultPosition { get; set; } = "TOP_LEFT";
    public string? AltText { get; set; }
    public string FileUrl => $"/Administration/BrandAssets/{Id}/File";
}

public sealed class BrandAssetEditInput
{
    public Guid Id { get; set; }
    [Required, StringLength(160)] public string BrandName { get; set; } = "";
    [Required, StringLength(160)] public string AssetName { get; set; } = "";
    [Range(10, 90)] public decimal DefaultWidthMm { get; set; } = 38;
    [Range(5, 60)] public decimal? DefaultHeightMm { get; set; }
    public bool PreserveAspectRatio { get; set; } = true;
    [RegularExpression("CONTAIN|COVER|FILL")] public string FitMode { get; set; } = "CONTAIN";
    [RegularExpression("TOP_LEFT|TOP_CENTER|TOP_RIGHT|CENTER|BOTTOM_LEFT|BOTTOM_CENTER|BOTTOM_RIGHT")] public string DefaultPosition { get; set; } = "TOP_LEFT";
    public bool IsDefault { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    [StringLength(300)] public string? AltText { get; set; }
    public string FileUrl => $"/Administration/BrandAssets/{Id}/File";
}

public sealed class PrintLogoViewModel
{
    public string? LogoUrl { get; set; }
    public string Alt { get; set; } = "Logo oficial";
    public string? CssClass { get; set; }
    public decimal WidthMm { get; set; } = 38;
    public decimal? HeightMm { get; set; }
    public bool PreserveAspectRatio { get; set; } = true;
    public string FitMode { get; set; } = "CONTAIN";
}

public sealed class PrintableDocumentHeaderViewModel
{
    public PrintLogoViewModel? PrimaryLogo { get; set; }
    public PrintLogoViewModel? SecondaryLogo { get; set; }
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Footer { get; set; }
}
