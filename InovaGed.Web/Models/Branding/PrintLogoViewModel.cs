namespace InovaGed.Web.Models.Branding;

/// <summary>The presentation-only representation used by every Razor logo partial.</summary>
public sealed class PrintLogoViewModel : IPrintLogo
{
    public static PrintLogoViewModel Empty => new();
    public Guid? AssetId { get; init; }
    public string? BrandName { get; init; }
    public string? LogoUrl { get; init; }
    public string? PrintImageSource { get; init; }
    public string Alt { get; init; } = "Logo oficial";
    public string? CssClass { get; init; }
    public decimal WidthMm { get; init; } = 38;
    public decimal? HeightMm { get; init; }
    public bool PreserveAspectRatio { get; init; } = true;
    public string FitMode { get; init; } = "CONTAIN";
    public string Position { get; init; } = "TOP_LEFT";
    public decimal OffsetXmm { get; init; }
    public decimal OffsetYmm { get; init; }
    public bool HasLogo { get; init; }
    public bool ImageLoaded { get; init; }
    public string? LoadError { get; init; }
}
