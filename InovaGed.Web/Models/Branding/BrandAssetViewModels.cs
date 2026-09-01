using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Branding;

public sealed class BrandAssetUploadInput
{
    [Required, StringLength(160)] public string BrandName { get; set; } = "";
    [Required, StringLength(160)] public string AssetName { get; set; } = "";
    [Required] public IFormFile? File { get; set; }
    public bool IsDefault { get; set; }
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
}

public sealed class PrintableDocumentHeaderViewModel
{
    public PrintLogoViewModel? PrimaryLogo { get; set; }
    public PrintLogoViewModel? SecondaryLogo { get; set; }
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Footer { get; set; }
}
