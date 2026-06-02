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
        if (realFolder is not null && !isVirtual)
        {
            return new UploadFolderResolutionResult
            {
                Success = true,
                RequestedFolderId = folderId,
                ResolvedFolderId = realFolder.Id,
                WasVirtual = false,
                FolderName = realFolder.Name,
                FolderPath = realFolder.Name,
                Message = "Pasta resolvida para upload."
            };
        }

        if (realFolder is not null && isVirtual)
        {
            _logger.LogWarning("ID de pasta virtual existe em ged.folder, mas não será usado como destino persistível. Tenant={TenantId} User={UserId} FolderId={FolderId}", tenantId, userId, folderId);
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
                FolderName = mapped.Name,
                FolderPath = mapped.Name,
                Message = "Destino virtual resolvido automaticamente."
            };
        }

        if (mapped is not null && mapped.Id == folderId)
        {
            _logger.LogWarning("Mapeamento de pasta virtual aponta para o próprio ID virtual e será ignorado. Tenant={TenantId} User={UserId} FolderId={FolderId}", tenantId, userId, folderId);
        }

        try
        {
            var created = await CreateMappedFolderAsync(conn, tenantId, userId, folderId, realFolder?.Name, ct);
            if (created.Id == folderId)
            {
                _logger.LogError("Resolver de pasta virtual tentou usar o ID virtual como pasta real. Tenant={TenantId} User={UserId} FolderId={FolderId}", tenantId, userId, folderId);
                return UploadFolderResolutionResult.Fail(folderId, "Não foi possível resolver a pasta selecionada para upload.");
            }

            _logger.LogInformation("Pasta virtual resolvida com criação automática. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} ResolvedFolderId={ResolvedFolderId} IsAdmin={IsAdmin}", tenantId, userId, folderId, created.Id, isAdmin);

            return new UploadFolderResolutionResult
            {
                Success = true,
                RequestedFolderId = folderId,
                ResolvedFolderId = created.Id,
                WasVirtual = true,
                CreatedRealFolder = true,
                FolderName = created.Name,
                FolderPath = created.Name,
                Message = "Destino ajustado automaticamente pelo sistema."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Não foi possível criar ou mapear pasta real para pasta virtual. Tenant={TenantId} User={UserId} FolderId={FolderId}", tenantId, userId, folderId);
            return UploadFolderResolutionResult.Fail(folderId, "Não foi possível resolver a pasta selecionada para upload.");
        }
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

    private static async Task<FolderRow> CreateMappedFolderAsync(System.Data.IDbConnection conn, Guid tenantId, Guid userId, Guid virtualFolderId, string? virtualFolderName, CancellationToken ct)
    {
        var realFolderId = Guid.NewGuid();
        var mapId = Guid.NewGuid();
        var suffix = virtualFolderId.ToString("N")[^6..];
        var name = !string.IsNullOrWhiteSpace(virtualFolderName)
            ? $"Uploads - {virtualFolderName}"
            : $"Uploads - {suffix}";

        using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(hashtext(@key));", new { key = $"upload-folder-map:{tenantId:N}:{virtualFolderId:N}" }, tx, cancellationToken: ct));

            var existing = await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
SELECT f.id, f.name, f.parent_id AS ParentId
FROM ged.folder_virtual_map m
JOIN ged.folder f ON f.tenant_id=m.tenant_id AND f.id=m.real_folder_id
WHERE m.tenant_id=@tenantId AND m.virtual_folder_id=@virtualFolderId AND m.reg_status='A'
LIMIT 1;
""", new { tenantId, virtualFolderId }, tx, cancellationToken: ct));
            if (existing is not null)
            {
                tx.Commit();
                return existing;
            }

            await conn.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.folder (id, tenant_id, name, parent_id, is_active, created_at, created_by, reg_status)
VALUES (@realFolderId, @tenantId, @name, NULL, TRUE, now(), @userId, 'A');

INSERT INTO ged.folder_virtual_map (id, tenant_id, virtual_folder_id, real_folder_id, created_by, reg_status)
VALUES (@mapId, @tenantId, @virtualFolderId, @realFolderId, @userId, 'A');
""", new { realFolderId, mapId, tenantId, virtualFolderId, userId, name }, tx, cancellationToken: ct));

            tx.Commit();
            return new FolderRow { Id = realFolderId, Name = name };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private sealed class FolderRow
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
