using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Folders;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Folders;

public sealed class GedFolderMoveService : IGedFolderMoveService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GedFolderMoveService> _logger;

    public GedFolderMoveService(IDbConnectionFactory db, IAuditWriter audit, IMemoryCache cache, ILogger<GedFolderMoveService> logger)
    {
        _db = db;
        _audit = audit;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<MoveFolderResult>> MoveAsync(Guid tenantId, Guid userId, MoveFolderRequest request, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) return Result<MoveFolderResult>.Fail("TENANT", "Tenant inválido.");
        if (userId == Guid.Empty) return Result<MoveFolderResult>.Fail("USER", "Usuário inválido.");
        if (request.FolderId == Guid.Empty) return Result<MoveFolderResult>.Fail("VALIDATION", "Pasta de origem obrigatória.");
        if (FolderIdHelper.IsVirtualFolder(request.FolderId)) return Result<MoveFolderResult>.Fail("VIRTUAL", "Não é permitido mover pasta virtual.");
        if (request.DestinationParentId.HasValue && FolderIdHelper.IsVirtualFolder(request.DestinationParentId.Value)) return Result<MoveFolderResult>.Fail("VIRTUAL_DESTINATION", "Não é permitido mover para uma pasta virtual.");
        if (request.DestinationParentId == request.FolderId) return Result<MoveFolderResult>.Fail("CYCLE", "Não é possível mover uma pasta para ela mesma.");

        await using var conn = await _db.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            var source = await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
select id, tenant_id as TenantId, parent_id as ParentId, name, reg_status as RegStatus
from ged.folder
where tenant_id = @tenantId
  and id = @folderId
  and coalesce(reg_status,'A') = 'A'
for update;
""", new { tenantId, folderId = request.FolderId }, tx, cancellationToken: ct));
            if (source is null) return RollbackFail("NOT_FOUND", "Pasta de origem não encontrada ou inativa.");

            _logger.LogInformation("Movendo pasta. FolderId={FolderId} OldParentId={OldParentId} DestinationParentId={DestinationParentId}", request.FolderId, source.ParentId, request.DestinationParentId);

            if (source.ParentId == request.DestinationParentId)
                return RollbackFail("SAME_DESTINATION", "A pasta já está neste destino.");

            if (request.DestinationParentId.HasValue)
            {
                var destination = await conn.QuerySingleOrDefaultAsync<FolderRow>(new CommandDefinition("""
select id, tenant_id as TenantId, parent_id as ParentId, name, reg_status as RegStatus
from ged.folder
where tenant_id = @tenantId
  and id = @destinationParentId
  and coalesce(reg_status,'A') = 'A';
""", new { tenantId, destinationParentId = request.DestinationParentId.Value }, tx, cancellationToken: ct));
                if (destination is null) return RollbackFail("DESTINATION_NOT_FOUND", "Pasta de destino não encontrada ou inativa.");

                var isDescendant = await IsDescendantAsync(conn, tx, tenantId, request.FolderId, request.DestinationParentId.Value, ct);
                if (isDescendant) return RollbackFail("CYCLE", "Não é possível mover uma pasta para uma subpasta dela.");
            }

            var duplicate = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from ged.folder
    where tenant_id = @tenantId
      and coalesce(reg_status,'A') = 'A'
      and id <> @folderId
      and lower(name) = lower(@folderName)
      and (
          (@destinationParentId is null and parent_id is null)
          or parent_id = @destinationParentId
      )
);
""", new { tenantId, folderName = source.Name, destinationParentId = request.DestinationParentId, folderId = request.FolderId }, tx, cancellationToken: ct));
            if (duplicate) return RollbackFail("DUPLICATE", "Já existe uma pasta com esse nome no destino.");

            var oldPath = await GetFolderPathAsync(conn, tx, tenantId, request.FolderId, ct);
            var newPath = await BuildMovedFolderPathAsync(conn, tx, tenantId, request.DestinationParentId, source.Name, ct);
            var rowsAffected = await conn.ExecuteAsync(new CommandDefinition("""
update ged.folder
set parent_id = @destinationParentId,
    updated_at = now(),
    updated_by = @userId
where tenant_id = @tenantId
  and id = @folderId
  and coalesce(reg_status,'A') = 'A';
""", new { tenantId, folderId = request.FolderId, destinationParentId = request.DestinationParentId, userId }, tx, cancellationToken: ct));
            if (rowsAffected == 0)
                return RollbackFail("NOT_MOVED", "Não foi possível mover a pasta. A pasta pode ter sido alterada por outro usuário.");

            var confirmedParentId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
select parent_id
from ged.folder
where tenant_id = @tenantId
  and id = @folderId;
""", new { tenantId, folderId = request.FolderId }, tx, cancellationToken: ct));

            _logger.LogInformation("Pasta movida no banco. FolderId={FolderId} RowsAffected={RowsAffected} ConfirmedParentId={ConfirmedParentId}", request.FolderId, rowsAffected, confirmedParentId);

            if (confirmedParentId != request.DestinationParentId)
                return RollbackFail("NOT_CONFIRMED", "A movimentação não foi confirmada no banco.");

            var result = new MoveFolderResult { FolderId = request.FolderId, OldParentId = source.ParentId, NewParentId = confirmedParentId, OldPath = oldPath, NewPath = newPath, Moved = true, RowsAffected = rowsAffected };

            tx.Commit();
            _cache.Remove($"ged:folders:{tenantId}");

            try
            {
                await _audit.WriteAsync(tenantId, userId, "GED_FOLDER_MOVED", "ged.folder", request.FolderId, "Pasta movida no GED.", request.IpAddress, request.UserAgent, new { folderId = request.FolderId, folderName = source.Name, oldParentId = source.ParentId, newParentId = confirmedParentId, oldPath, newPath, reason = request.Reason, movedBy = userId, tenantId, rowsAffected }, ct);
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Falha ao auditar movimentação de pasta. FolderId={FolderId}", request.FolderId);
            }

            return Result<MoveFolderResult>.Ok(result);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Erro ao mover pasta GED. Tenant={TenantId} Folder={FolderId}", tenantId, request.FolderId);
            return Result<MoveFolderResult>.Fail("ERR", "Não foi possível mover a pasta.");
        }

        Result<MoveFolderResult> RollbackFail(string code, string message)
        {
            _logger.LogWarning("Falha ao mover pasta. Motivo={Reason}", message);
            tx.Rollback();
            return Result<MoveFolderResult>.Fail(code, message);
        }
    }

    public async Task<IReadOnlyList<MoveFolderTarget>> GetMoveTargetsAsync(Guid tenantId, Guid userId, Guid folderId, bool includeRoot, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<MoveFolderTarget>(new CommandDefinition("""
with recursive descendants as (
    select id
    from ged.folder
    where tenant_id = @tenantId
      and id = @folderId
      and coalesce(reg_status, 'A') = 'A'

    union all

    select f.id
    from ged.folder f
    join descendants d on f.parent_id = d.id
    where f.tenant_id = @tenantId
      and coalesce(f.reg_status, 'A') = 'A'
),
folder_tree as (
    select
        f.id,
        f.parent_id,
        f.name,
        f.name::text as full_path,
        0 as level
    from ged.folder f
    where f.tenant_id = @tenantId
      and f.parent_id is null
      and coalesce(f.reg_status, 'A') = 'A'

    union all

    select
        c.id,
        c.parent_id,
        c.name,
        (ft.full_path || ' > ' || c.name)::text as full_path,
        ft.level + 1 as level
    from ged.folder c
    join folder_tree ft on ft.id = c.parent_id
    where c.tenant_id = @tenantId
      and coalesce(c.reg_status, 'A') = 'A'
)
select
    ft.id as "Id",
    ft.parent_id as "ParentId",
    ft.name as "Name",
    ft.full_path as "Path",
    ft.level as "Level"
from folder_tree ft
where ft.id <> @folderId
  and not exists (
      select 1
      from descendants d
      where d.id = ft.id
  )
order by ft.full_path;
""", new { tenantId, folderId }, cancellationToken: ct))).AsList();
        if (includeRoot) rows.Insert(0, new MoveFolderTarget { Id = null, ParentId = null, Name = "Raiz", Path = "Raiz", Level = 0 });
        return rows;
    }

    private static async Task<bool> IsDescendantAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid tenantId, Guid folderId, Guid destinationParentId, CancellationToken ct)
        => await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
with recursive descendants as (
    select id
    from ged.folder
    where tenant_id = @tenantId
      and id = @folderId
      and coalesce(reg_status,'A') = 'A'

    union all

    select f.id
    from ged.folder f
    join descendants d on f.parent_id = d.id
    where f.tenant_id = @tenantId
      and coalesce(f.reg_status,'A') = 'A'
)
select exists (select 1 from descendants where id = @destinationParentId);
""", new { tenantId, folderId, destinationParentId }, tx, cancellationToken: ct));

    private static async Task<string?> GetFolderPathAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid tenantId, Guid folderId, CancellationToken ct)
        => await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
with recursive ancestors as (
    select id, parent_id, name, 0 as depth
    from ged.folder
    where tenant_id = @tenantId
      and id = @folderId
      and coalesce(reg_status,'A') = 'A'

    union all

    select p.id, p.parent_id, p.name, a.depth + 1
    from ged.folder p
    join ancestors a on a.parent_id = p.id
    where p.tenant_id = @tenantId
      and coalesce(p.reg_status,'A') = 'A'
)
select string_agg(name, ' > ' order by depth desc)
from ancestors;
""", new { tenantId, folderId }, tx, cancellationToken: ct));

    private static async Task<string> BuildMovedFolderPathAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid tenantId, Guid? destinationParentId, string folderName, CancellationToken ct)
    {
        if (!destinationParentId.HasValue) return folderName;

        var destinationPath = await GetFolderPathAsync(conn, tx, tenantId, destinationParentId.Value, ct);
        return string.IsNullOrWhiteSpace(destinationPath) ? folderName : destinationPath + " > " + folderName;
    }

    private sealed class FolderRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string RegStatus { get; set; } = "A";
    }
}
