using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Audit;
using InovaGed.Application.Security;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class DocumentMoveService : IDocumentMoveService
{
    private const int BulkLimit = 200;
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<DocumentMoveService> _logger;
    private readonly IPermissionService _permissionService;

    public DocumentMoveService(IDbConnectionFactory db, IAuditWriter audit, ILogger<DocumentMoveService> logger, IPermissionService permissionService)
    { _db = db; _audit = audit; _logger = logger; _permissionService = permissionService; }

    public async Task<Result<DocumentMoveResultDto>> MoveAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, CancellationToken ct)
    {
        try
        {
            var item = await MoveOneAsync(tenantId, userId, userName, documentId, destinationFolderId, reason, source, null, ct);
            return item.Success ? Result<DocumentMoveResultDto>.Ok(item) : Result<DocumentMoveResultDto>.Fail(item.ErrorCode ?? "MOVE", item.Message ?? "Falha ao mover documento.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em MoveAsync Tenant={TenantId} User={UserId} Document={DocumentId} Destination={DestinationFolderId}", tenantId, userId, documentId, destinationFolderId);
            return Result<DocumentMoveResultDto>.Fail("MOVE", "Erro interno ao mover documento.");
        }
    }

    public async Task<Result<DocumentBulkMoveResultDto>> MoveBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, CancellationToken ct)
    {
        try
        {
            if (documentIds.Count == 0) return Result<DocumentBulkMoveResultDto>.Fail("VALIDATION", "Nenhum documento selecionado.");
            if (documentIds.Count > BulkLimit) return Result<DocumentBulkMoveResultDto>.Fail("LIMIT", $"Limite máximo de {BulkLimit} documentos por operação.");
            var batchId = Guid.NewGuid();
            var items = new List<DocumentMoveResultDto>(documentIds.Count);
            foreach (var id in documentIds.Distinct()) items.Add(await MoveOneAsync(tenantId, userId, userName, id, destinationFolderId, reason, source, batchId, ct));
            var ok = items.Count(i => i.Success);
            await _audit.WriteAsync(tenantId, userId, "MOVE_DOCUMENT_FOLDER_BULK", "DOCUMENT", batchId, "Documentos movidos em lote", null, null, new { batchId, total = items.Count, successCount = ok, failCount = items.Count - ok, destinationFolderId, reason, source = "BULK" }, ct);
            return Result<DocumentBulkMoveResultDto>.Ok(new DocumentBulkMoveResultDto { BatchId = batchId, Total = items.Count, SuccessCount = ok, FailCount = items.Count - ok, Items = items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em MoveBulkAsync Tenant={TenantId} User={UserId} Destination={DestinationFolderId} BatchSize={BatchSize}", tenantId, userId, destinationFolderId, documentIds.Count);
            return Result<DocumentBulkMoveResultDto>.Fail("MOVE_BULK", "Erro interno ao mover documentos.");
        }
    }

    public async Task<IReadOnlyList<FolderOptionDto>> SearchFoldersAsync(Guid tenantId, Guid userId, string? term, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var normalizedTerm = string.IsNullOrWhiteSpace(term) ? null : term.Trim();

            const string sql = """
with recursive folder_tree as (
    select f.id, f.parent_id, f.name, f.tenant_id, f.name::text as full_path
    from ged.folder f
    where f.tenant_id = @tenantId
      and f.parent_id is null
      and f.is_active = true
      and f.reg_status = 'A'
    union all
    select c.id, c.parent_id, c.name, c.tenant_id, (ft.full_path || ' > ' || c.name)::text as full_path
    from ged.folder c
    join folder_tree ft on ft.id = c.parent_id and ft.tenant_id = c.tenant_id
    where c.tenant_id = @tenantId
      and c.is_active = true
      and c.reg_status = 'A'
)
select id as Id, name as Name, full_path as FullPath, parent_id as ParentId
from folder_tree
where @term is null
   or name ilike ('%' || @term || '%')
   or full_path ilike ('%' || @term || '%')
order by full_path
limit 30;
""";

            return (await conn.QueryAsync<FolderOptionDto>(
                new CommandDefinition(sql, new { tenantId, term = normalizedTerm }, cancellationToken: ct))).AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao buscar pastas para movimentação. Tenant={TenantId} User={UserId}", tenantId, userId);
            return Array.Empty<FolderOptionDto>();
        }
    }

    public async Task<IReadOnlyList<DocumentMoveHistoryDto>> GetMoveHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            const string sql = """
select h.id,h.document_id as DocumentId,h.old_folder_id as OldFolderId,h.new_folder_id as NewFolderId,
fo.name as OldFolderName, fn.name as NewFolderName, h.moved_by as MovedBy,h.moved_by_name as MovedByName,
h.moved_at as MovedAt,h.reason,h.source
from ged.document_folder_move_history h
left join ged.folder fo on fo.tenant_id=h.tenant_id and fo.id=h.old_folder_id
left join ged.folder fn on fn.tenant_id=h.tenant_id and fn.id=h.new_folder_id
where h.tenant_id=@tenantId and h.document_id=@documentId and h.reg_status='A' order by h.moved_at desc;
""";
            return (await conn.QueryAsync<DocumentMoveHistoryDto>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct))).AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em GetMoveHistoryAsync Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            return Array.Empty<DocumentMoveHistoryDto>();
        }
    }

    private async Task<DocumentMoveResultDto> MoveOneAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, Guid? batchId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var doc = await conn.QueryFirstOrDefaultAsync<(Guid Id, Guid? FolderId, string RegStatus, bool IsConfidential, bool IsDeleted)>(new CommandDefinition("select id, folder_id as FolderId, reg_status as RegStatus, is_confidential as IsConfidential, coalesce(is_deleted,false) as IsDeleted from ged.document where tenant_id=@tenantId and id=@documentId", new { tenantId, documentId }, cancellationToken: ct));
            if (doc.Id == Guid.Empty) return Fail(documentId, "Documento não encontrado.");
            if (!string.Equals(doc.RegStatus, "A", StringComparison.OrdinalIgnoreCase)) return Fail(documentId, "Documento inativo.");
            if (doc.IsDeleted) return Fail(documentId, "Documento excluído.");
            if (doc.FolderId == destinationFolderId) return Fail(documentId, "Documento já está na pasta destino.");
            if (!await CanMoveAsync(conn, tenantId, userId, doc.IsConfidential, ct))
            {
                await _audit.WriteAsync(tenantId, userId, "ACCESS_DENIED_MOVE_DOCUMENT", "DOCUMENT", documentId, "Tentativa não autorizada de mover documento", null, null, new { destinationFolderId, source, reason }, ct);
                return Denied(documentId, "Usuário sem permissão para mover este documento.");
            }
            var destinationExists = await conn.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("select id from ged.folder where tenant_id=@tenantId and id=@destinationFolderId and reg_status='A' and is_active=true", new { tenantId, destinationFolderId }, cancellationToken: ct));
            if (!destinationExists.HasValue) return Fail(documentId, "Pasta destino inválida.");
            await conn.ExecuteAsync(new CommandDefinition("update ged.document set folder_id=@destinationFolderId, updated_at=now(), updated_by=@userId where tenant_id=@tenantId and id=@documentId", new { tenantId, documentId, destinationFolderId, userId }, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("insert into ged.document_folder_move_history (id, tenant_id, document_id, old_folder_id, new_folder_id, moved_by, moved_by_name, reason, batch_id, source, reg_status) values (@id,@tenantId,@documentId,@oldFolderId,@newFolderId,@movedBy,@movedByName,@reason,@batchId,@source,'A')", new { id = Guid.NewGuid(), tenantId, documentId, oldFolderId = doc.FolderId, newFolderId = destinationFolderId, movedBy = userId, movedByName = userName, reason, batchId, source }, cancellationToken: ct));
            await _audit.WriteAsync(tenantId, userId, "MOVE_DOCUMENT_FOLDER", "DOCUMENT", documentId, "Documento movido de pasta", null, null, new { oldFolderId = doc.FolderId, newFolderId = destinationFolderId, reason, source }, ct);
            return new DocumentMoveResultDto { DocumentId = documentId, Success = true, Message = "Documento movido com sucesso.", OldFolderId = doc.FolderId, NewFolderId = destinationFolderId };
        }
        catch (Exception ex)
        { _logger.LogError(ex, "Erro em MoveOneAsync Tenant={TenantId} User={UserId} Document={DocumentId} Destination={Destination}", tenantId, userId, documentId, destinationFolderId); return Fail(documentId, "Erro interno ao mover documento."); }
    }
    private async Task<bool> CanMoveAsync(System.Data.Common.DbConnection conn, Guid tenantId, Guid userId, bool isConfidential, CancellationToken ct)
    {
        var roles = await GetUserRolesAsync(conn, tenantId, userId, ct);
        if (roles.Contains("ADMIN")) return true;
        if (roles.Contains("ADMINISTRADOROPHIR") || roles.Contains("ARQUIVISTAOPHIR"))
            return !isConfidential || await _permissionService.HasAsync(tenantId, userId, "GED_DOCUMENT_MOVE_CONFIDENTIAL", ct);
        return await _permissionService.HasAsync(tenantId, userId, "GED_DOCUMENT_MOVE", ct) && (!isConfidential || await _permissionService.HasAsync(tenantId, userId, "GED_DOCUMENT_MOVE_CONFIDENTIAL", ct));
    }

    private static async Task<HashSet<string>> GetUserRolesAsync(System.Data.Common.DbConnection conn, Guid tenantId, Guid userId, CancellationToken ct)
    {
        const string sql = """
select r.normalized_name
from ged.user_roles ur
join ged.app_role r on r.id = ur.role_id and r.tenant_id = ur.tenant_id
where ur.tenant_id = @tenantId
  and ur.user_id = @userId
""";

        return (await conn.QueryAsync<string>(new CommandDefinition(sql, new { tenantId, userId }, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static DocumentMoveResultDto Fail(Guid id, string msg) => new() { DocumentId = id, Success = false, Message = msg, ErrorCode = "VALIDATION" };
    private static DocumentMoveResultDto Denied(Guid id, string msg) => new() { DocumentId = id, Success = false, Message = msg, ErrorCode = "ACCESS_DENIED" };
}
