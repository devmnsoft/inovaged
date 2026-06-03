using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Folders;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Folders;

public sealed class UploadFolderResolver : IUploadFolderResolver
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<UploadFolderResolver> _logger;

    public UploadFolderResolver(IDbConnectionFactory db, ILogger<UploadFolderResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UploadFolderResolutionResult> ResolveAsync(Guid tenantId, Guid userId, Guid folderId, bool isAdmin, CancellationToken ct)
    {
        if (folderId == Guid.Empty)
        {
            return UploadFolderResolutionResult.Fail(folderId, "Selecione uma pasta para enviar documentos.");
        }

        await using var conn = await _db.OpenAsync(ct);
        var realFolder = await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
SELECT id, name, parent_id AS ParentId
FROM ged.folder
WHERE tenant_id=@tenantId AND id=@folderId AND is_active=true AND coalesce(reg_status,'A')='A';
""", new { tenantId, folderId }, cancellationToken: ct));

        var isVirtual = FolderIdHelper.IsVirtualFolder(folderId);
        if (isVirtual)
        {
            await EnsureVirtualMapTableAsync(conn, ct);
            var preferredMapped = await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
SELECT f.id, f.name, f.parent_id AS ParentId
FROM ged.folder_virtual_map m
JOIN ged.folder f ON f.tenant_id=m.tenant_id AND f.id=m.real_folder_id
WHERE m.tenant_id=@tenantId
  AND m.virtual_folder_id=@folderId
  AND m.reg_status='A'
  AND f.is_active=true
  AND coalesce(f.reg_status,'A')='A'
LIMIT 1;
""", new { tenantId, folderId }, cancellationToken: ct));
            if (preferredMapped is not null && preferredMapped.Id != folderId)
            {
                return new UploadFolderResolutionResult
                {
                    Success = true,
                    RequestedFolderId = folderId,
                    ResolvedFolderId = preferredMapped.Id,
                    WasVirtual = true,
                    CreatedRealFolder = false,
                    CanReceiveDocuments = true,
                    FolderName = preferredMapped.Name,
                    FolderPath = preferredMapped.Name,
                    Message = "Destino virtual resolvido automaticamente."
                };
            }
        }

        if (realFolder is not null)
        {
            _logger.LogInformation("Pasta de upload existe em ged.folder e será usada como destino persistível. Tenant={TenantId} User={UserId} FolderId={FolderId} WasVirtual={WasVirtual} FolderName={FolderName}", tenantId, userId, folderId, isVirtual, realFolder.Name);
            return new UploadFolderResolutionResult
            {
                Success = true,
                RequestedFolderId = folderId,
                ResolvedFolderId = realFolder.Id,
                WasVirtual = isVirtual,
                CreatedRealFolder = false,
                CanReceiveDocuments = true,
                FolderName = realFolder.Name,
                FolderPath = realFolder.Name,
                Message = "Pasta resolvida para upload."
            };
        }

        if (!isVirtual)
        {
            return UploadFolderResolutionResult.Fail(folderId, "A pasta selecionada não foi encontrada. Atualize a tela e selecione novamente.");
        }

        await EnsureVirtualMapTableAsync(conn, ct);

        var mapped = await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
SELECT f.id, f.name, f.parent_id AS ParentId
FROM ged.folder_virtual_map m
JOIN ged.folder f ON f.tenant_id=m.tenant_id AND f.id=m.real_folder_id
WHERE m.tenant_id=@tenantId
  AND m.virtual_folder_id=@folderId
  AND m.reg_status='A'
  AND f.is_active=true
  AND coalesce(f.reg_status,'A')='A'
LIMIT 1;
""", new { tenantId, folderId }, cancellationToken: ct));

        if (mapped is not null && mapped.Id != folderId)
        {
            return new UploadFolderResolutionResult
            {
                Success = true,
                RequestedFolderId = folderId,
                ResolvedFolderId = mapped.Id,
                WasVirtual = true,
                CreatedRealFolder = false,
                CanReceiveDocuments = true,
                FolderName = mapped.Name,
                FolderPath = mapped.Name,
                Message = "Destino virtual resolvido automaticamente."
            };
        }

        if (mapped is not null && mapped.Id == folderId)
        {
            _logger.LogWarning("Mapeamento de pasta virtual aponta para o próprio ID virtual e será ignorado. Tenant={TenantId} User={UserId} FolderId={FolderId}", tenantId, userId, folderId);
        }

        _logger.LogWarning("Pasta virtual/visual sem mapeamento real válido e sem registro em ged.folder. Tenant={TenantId} User={UserId} FolderId={FolderId} IsAdmin={IsAdmin}", tenantId, userId, folderId, isAdmin);
        return UploadFolderResolutionResult.Fail(folderId, "A pasta selecionada não possui destino de upload válido. Atualize a página e selecione novamente.");
    }

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
