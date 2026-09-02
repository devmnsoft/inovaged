namespace InovaGed.Application.Branding;

public interface IBrandAssetImageService
{
    Task<BrandAssetImageResult?> GetImageAsync(Guid tenantId, Guid assetId, CancellationToken ct);
    Task<string?> GetDataUriAsync(Guid tenantId, Guid assetId, CancellationToken ct);
}

public sealed record BrandAssetImageResult(Guid AssetId, string BrandName, string ContentType,
    string StorageRelativePath, byte[] Bytes, string DataUri, int? WidthPx, int? HeightPx);
