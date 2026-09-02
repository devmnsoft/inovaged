using Dapper;
using InovaGed.Application.Branding;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Branding;

public sealed class BrandAssetImageService(IDbConnectionFactory factory, IHostEnvironment environment,
    ILogger<BrandAssetImageService> logger) : IBrandAssetImageService
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/png", "image/jpeg", "image/webp" };

    public async Task<BrandAssetImageResult?> GetImageAsync(Guid tenantId, Guid assetId, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var asset = await db.QuerySingleOrDefaultAsync<AssetRow>(new CommandDefinition("""
            select id, brand_name as BrandName, content_type as ContentType,
                   storage_relative_path as StorageRelativePath, width_px as WidthPx, height_px as HeightPx
            from ged.brand_asset
            where id=@assetId and tenant_id=@tenantId and status='ACTIVE' and reg_status='A'
            """, new { tenantId, assetId }, cancellationToken: ct));
        if (asset is null || !AllowedContentTypes.Contains(asset.ContentType)) return null;

        var path = ResolveSafePath(asset.StorageRelativePath);
        if (path is null || !File.Exists(path))
        {
            logger.LogWarning("Active brand asset {AssetId} for tenant {TenantId} has no readable stored file.", assetId, tenantId);
            return null;
        }
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.Length == 0) return null;
        var dataUri = $"data:{asset.ContentType};base64,{Convert.ToBase64String(bytes)}";
        return new(asset.Id, asset.BrandName, asset.ContentType, asset.StorageRelativePath, bytes, dataUri, asset.WidthPx, asset.HeightPx);
    }

    public async Task<string?> GetDataUriAsync(Guid tenantId, Guid assetId, CancellationToken ct)
        => (await GetImageAsync(tenantId, assetId, ct))?.DataUri;

    private string? ResolveSafePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root, StringComparison.Ordinal) ? path : null;
    }

    private sealed class AssetRow
    {
        public Guid Id { get; init; }
        public string BrandName { get; init; } = "";
        public string ContentType { get; init; } = "";
        public string StorageRelativePath { get; init; } = "";
        public int? WidthPx { get; init; }
        public int? HeightPx { get; init; }
    }
}
