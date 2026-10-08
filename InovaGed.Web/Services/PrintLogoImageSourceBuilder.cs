using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;

namespace InovaGed.Web.Services;

public sealed class PrintLogoImageSourceBuilder(IDbConnectionFactory db, IFileStorage storage)
{
    public async Task<string?> BuildLogoSourceAsync(Guid tenantId, Guid? assetId, CancellationToken ct)
    {
        if (!assetId.HasValue || assetId.Value == Guid.Empty) return null;

        await using var conn = await db.OpenAsync(ct);
        var asset = await conn.QuerySingleOrDefaultAsync<LogoAssetRow>(new CommandDefinition("""
select id, tenant_id as "TenantId", storage_path as "StoragePath", content_type as "ContentType", status
from ged.brand_asset
where tenant_id=@tenantId and id=@assetId and status='ACTIVE' and coalesce(reg_status,'A')='A';
""", new { tenantId, assetId = assetId.Value }, cancellationToken: ct));

        if (asset is null || string.IsNullOrWhiteSpace(asset.StoragePath)) return null;

        if (Path.IsPathRooted(asset.StoragePath) && File.Exists(asset.StoragePath))
        {
            var bytes = await File.ReadAllBytesAsync(asset.StoragePath, ct);
            return $"data:{asset.ContentType};base64,{Convert.ToBase64String(bytes)}";
        }

        if (await storage.ExistsAsync(asset.StoragePath, ct))
        {
            await using var stream = await storage.OpenReadAsync(asset.StoragePath, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return $"data:{asset.ContentType};base64,{Convert.ToBase64String(ms.ToArray())}";
        }

        return null;
    }

    private sealed class LogoAssetRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string? StoragePath { get; set; }
        public string ContentType { get; set; } = "image/png";
        public string Status { get; set; } = "ACTIVE";
    }
}
