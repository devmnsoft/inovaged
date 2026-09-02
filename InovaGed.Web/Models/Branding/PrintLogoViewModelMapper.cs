namespace InovaGed.Web.Models.Branding;

public static class PrintLogoViewModelMapper
{
    public static PrintLogoViewModel FromResolved(ResolvedPrintLogo? logo)
    {
        if (logo is null) return new PrintLogoViewModel();
        return new PrintLogoViewModel
        {
            AssetId = logo.AssetId, BrandName = logo.BrandName, LogoUrl = logo.LogoUrl,
            PrintImageSource = logo.PrintImageSource,
            Alt = string.IsNullOrWhiteSpace(logo.Alt) ? "Logo oficial" : logo.Alt,
            WidthMm = logo.WidthMm <= 0 ? 38 : logo.WidthMm, HeightMm = logo.HeightMm,
            PreserveAspectRatio = logo.PreserveAspectRatio,
            FitMode = string.IsNullOrWhiteSpace(logo.FitMode) ? "CONTAIN" : logo.FitMode,
            Position = string.IsNullOrWhiteSpace(logo.Position) ? "TOP_LEFT" : logo.Position,
            OffsetXmm = logo.OffsetXmm, OffsetYmm = logo.OffsetYmm,
            HasLogo = logo.HasLogo, ImageLoaded = logo.ImageLoaded, LoadError = logo.LoadError
        };
    }
}
