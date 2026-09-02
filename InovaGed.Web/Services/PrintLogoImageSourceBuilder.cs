using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Services;

public interface IPrintLogoImageSourceBuilder
{
    Task<string?> BuildWebUrlAsync(Guid tenantId, Guid logoAssetId, IUrlHelper url, CancellationToken ct);
    Task<string?> BuildPrintImageSourceAsync(Guid tenantId, Guid logoAssetId, IUrlHelper url, CancellationToken ct);
}

public sealed class PrintLogoImageSourceBuilder(IDbConnectionFactory factory, IWebHostEnvironment environment) : IPrintLogoImageSourceBuilder
{
    private const long DataUriLimitBytes = 1024 * 1024;

    public async Task<string?> BuildWebUrlAsync(Guid tenantId, Guid logoAssetId, IUrlHelper url, CancellationToken ct)
        => await FindActiveAssetAsync(tenantId, logoAssetId, ct) is null
            ? null
            : url.Action("File", "BrandAssets", new { id = logoAssetId });

    public async Task<string?> BuildPrintImageSourceAsync(Guid tenantId, Guid logoAssetId, IUrlHelper url, CancellationToken ct)
    {
        var asset = await FindActiveAssetAsync(tenantId, logoAssetId, ct);
        if (asset is null) return null;
        var path = ResolveSafePath(asset.StorageRelativePath);
        if (path is null || !File.Exists(path)) return null;
        if (new FileInfo(path).Length > DataUriLimitBytes)
            return url.Action("File", "BrandAssets", new { id = logoAssetId });
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return $"data:{asset.ContentType};base64,{Convert.ToBase64String(bytes)}";
    }

    private async Task<AssetRow?> FindActiveAssetAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<AssetRow>(new CommandDefinition("""
            select storage_relative_path as StorageRelativePath, content_type as ContentType
            from ged.brand_asset
            where id=@id and tenant_id=@tenantId and status='ACTIVE' and reg_status='A'
            """, new { id, tenantId }, cancellationToken: ct));
    }

    private string? ResolveSafePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var root = Path.GetFullPath(environment.WebRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root, StringComparison.Ordinal) ? path : null;
    }

    private sealed class AssetRow
    {
        public string StorageRelativePath { get; set; } = "";
        public string ContentType { get; set; } = "image/png";
    }
}
