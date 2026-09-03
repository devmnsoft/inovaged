using InovaGed.Infrastructure.Branding;

namespace InovaGed.Application.Tests;

public sealed class BrandAssetImageServiceTests
{
    [Theory]
    [InlineData("data:image/png;base64,AQID", true)]
    [InlineData("DATA:IMAGE/JPEG;BASE64,AQID", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("data:,", false)]
    [InlineData("/data:,", false)]
    [InlineData("/uploads/logo.png", false)]
    [InlineData("data:image/png;base64,", false)]
    public void Print_image_source_requires_non_empty_image_base64_data_uri(string? source, bool expected)
        => Assert.Equal(expected, InovaGed.Application.Branding.ImageDataUriValidator.IsValidImageDataUri(source));

    [Theory]
    [InlineData("uploads/branding/tenant/logo.png", "uploads/branding/tenant/logo.png")]
    [InlineData("wwwroot\\uploads\\branding\\tenant\\logo.png", "uploads/branding/tenant/logo.png")]
    [InlineData("~/wwwroot/uploads/branding/tenant/logo.png", "uploads/branding/tenant/logo.png")]
    public void Legacy_storage_paths_are_normalized_to_safe_webroot_relative_paths(string stored, string expected)
        => Assert.Equal(expected, BrandAssetImageService.NormalizeStorageRelativePath(stored));

    [Theory]
    [InlineData("../../secret.png")]
    [InlineData("C:\\secret.png")]
    [InlineData("/etc/passwd")]
    public void Unsafe_storage_paths_are_rejected(string stored)
        => Assert.Null(BrandAssetImageService.NormalizeStorageRelativePath(stored));
}
