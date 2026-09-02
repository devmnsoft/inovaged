using InovaGed.Infrastructure.Branding;

namespace InovaGed.Application.Tests;

public sealed class BrandAssetImageServiceTests
{
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
