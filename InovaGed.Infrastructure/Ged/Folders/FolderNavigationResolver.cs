using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Folders;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Folders;

public sealed class FolderNavigationResolver : IFolderNavigationResolver
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<FolderNavigationResolver> _logger;

    public FolderNavigationResolver(IDbConnectionFactory db, ILogger<FolderNavigationResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<FolderNavigationResolution> ResolveForListingAsync(Guid tenantId, Guid userId, Guid? requestedFolderId, bool isAdmin, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await EnsureVirtualMapTableAsync(conn, ct);

        if (!requestedFolderId.HasValue || requestedFolderId.Value == Guid.Empty)
        {
            var fallback = await GetDefaultFolderAsync(conn, tenantId, ct);
            return FromFolder(requestedFolderId, fallback, fallback?.Id, false, success: fallback is not null);
        }

        var requested = requestedFolderId.Value;
        var isVirtualRequested = FolderIdHelper.IsVirtualFolder(requested);

        if (isVirtualRequested)
        {
            var mapped = await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
SELECT f.id, f.name, f.parent_id AS ParentId
FROM ged.folder_virtual_map m
JOIN ged.folder f ON f.tenant_id = m.tenant_id AND f.id = m.real_folder_id
WHERE m.tenant_id = @tenantId
  AND m.virtual_folder_id = @requested
  AND COALESCE(m.reg_status, 'A') = 'A'
  AND f.is_active = TRUE
  AND COALESCE(f.reg_status, 'A') = 'A'
LIMIT 1;
""", new { tenantId, requested }, cancellationToken: ct));

            if (mapped is not null && mapped.Id != Guid.Empty)
            {
                return new FolderNavigationResolution
                {
                    RequestedFolderId = requestedFolderId,
                    VisualFolderId = requested,
                    ListingFolderId = mapped.Id,
                    UploadFolderId = mapped.Id,
                    FolderName = mapped.Name,
                    WasVirtual = true,
                    Success = true
                };
            }
        }

        var realFolder = await GetFolderAsync(conn, tenantId, requested, ct);
        if (realFolder is not null)
        {
            return FromFolder(requestedFolderId, realFolder, requested, isVirtualRequested, success: true);
        }

        var defaultFolder = await GetDefaultFolderAsync(conn, tenantId, ct);
        _logger.LogWarning("GED navigation fallback aplicado. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} IsAdmin={IsAdmin} DefaultFolderId={DefaultFolderId}", tenantId, userId, requestedFolderId, isAdmin, defaultFolder?.Id);
        return FromFolder(requestedFolderId, defaultFolder, defaultFolder?.Id, false, success: defaultFolder is not null);
    }

    private static FolderNavigationResolution FromFolder(Guid? requestedFolderId, FolderRow? folder, Guid? visualFolderId, bool wasVirtual, bool success)
    {
        var id = folder?.Id ?? Guid.Empty;
        return new FolderNavigationResolution
        {
            RequestedFolderId = requestedFolderId,
            VisualFolderId = visualFolderId ?? id,
            ListingFolderId = id,
            UploadFolderId = id,
            FolderName = folder?.Name ?? "Documentos gerais",
            WasVirtual = wasVirtual,
            Success = success && id != Guid.Empty
        };
    }

    private static async Task<FolderRow?> GetFolderAsync(System.Data.IDbConnection conn, Guid tenantId, Guid folderId, CancellationToken ct)
        => await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
SELECT id, name, parent_id AS ParentId
FROM ged.folder
WHERE tenant_id = @tenantId
  AND id = @folderId
  AND is_active = TRUE
  AND COALESCE(reg_status, 'A') = 'A'
LIMIT 1;
""", new { tenantId, folderId }, cancellationToken: ct));

    private static async Task<FolderRow?> GetDefaultFolderAsync(System.Data.IDbConnection conn, Guid tenantId, CancellationToken ct)
        => await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
SELECT id, name, parent_id AS ParentId
FROM ged.folder
WHERE tenant_id = @tenantId
  AND is_active = TRUE
  AND COALESCE(reg_status, 'A') = 'A'
ORDER BY CASE WHEN parent_id IS NULL THEN 0 ELSE 1 END, name
LIMIT 1;
""", new { tenantId }, cancellationToken: ct));

    private static async Task EnsureVirtualMapTableAsync(System.Data.IDbConnection conn, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("""
CREATE TABLE IF NOT EXISTS ged.folder_virtual_map
(
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    virtual_folder_id uuid NOT NULL,
    real_folder_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_folder_virtual_map_active
ON ged.folder_virtual_map(tenant_id, virtual_folder_id)
WHERE reg_status='A';
CREATE INDEX IF NOT EXISTS ix_folder_virtual_map_real
ON ged.folder_virtual_map(tenant_id, real_folder_id)
WHERE reg_status='A';
""", cancellationToken: ct));
    }

    private sealed class FolderRow
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
