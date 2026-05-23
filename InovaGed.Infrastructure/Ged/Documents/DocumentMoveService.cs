using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Audit;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class DocumentMoveService : IDocumentMoveService
{
    private const int BulkLimit = 200;
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<DocumentMoveService> _logger;

    public DocumentMoveService(IDbConnectionFactory db, IAuditWriter audit, ILogger<DocumentMoveService> logger)
    { _db = db; _audit = audit; _logger = logger; }

    public async Task<Result<DocumentMoveResultDto>> MoveAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, CancellationToken ct)
    {
        var item = await MoveOneAsync(tenantId, userId, userName, documentId, destinationFolderId, reason, source, null, ct);
        return item.Success ? Result<DocumentMoveResultDto>.Ok(item) : Result<DocumentMoveResultDto>.Fail("MOVE", item.Message ?? "Falha ao mover documento.");
    }

    public async Task<Result<DocumentBulkMoveResultDto>> MoveBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, CancellationToken ct)
    {
        if (documentIds.Count == 0) return Result<DocumentBulkMoveResultDto>.Fail("VALIDATION", "Nenhum documento selecionado.");
        if (documentIds.Count > BulkLimit) return Result<DocumentBulkMoveResultDto>.Fail("LIMIT", $"Limite máximo de {BulkLimit} documentos por operação.");
        var batchId = Guid.NewGuid();
        var items = new List<DocumentMoveResultDto>(documentIds.Count);
        foreach (var id in documentIds.Distinct()) items.Add(await MoveOneAsync(tenantId, userId, userName, id, destinationFolderId, reason, source, batchId, ct));
        var ok = items.Count(i => i.Success);
        await _audit.WriteAsync(tenantId, userId, "UPDATE", "DOCUMENT", batchId, "Documentos movidos em lote", null, null, new { batchId, total = items.Count, successCount = ok, failCount = items.Count - ok, destinationFolderId, reason, source = "BULK" }, ct);
        return Result<DocumentBulkMoveResultDto>.Ok(new DocumentBulkMoveResultDto { BatchId = batchId, Total = items.Count, SuccessCount = ok, FailCount = items.Count - ok, Items = items });
    }

    public async Task<IReadOnlyList<FolderOptionDto>> SearchFoldersAsync(Guid tenantId, string? term, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        const string sql = """
select id as Id, name as Name, name as FullPath, parent_id as ParentId
from ged.folder where tenant_id=@tenantId and reg_status='A' and is_active=true
  and (@term is null or name ilike '%' || @term || '%')
order by name limit 50;
""";
        return (await conn.QueryAsync<FolderOptionDto>(new CommandDefinition(sql, new { tenantId, term = string.IsNullOrWhiteSpace(term) ? null : term.Trim() }, cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<DocumentMoveHistoryDto>> GetMoveHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct)
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

    private async Task<DocumentMoveResultDto> MoveOneAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, Guid? batchId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var doc = await conn.QueryFirstOrDefaultAsync<(Guid Id, Guid? FolderId, string RegStatus, bool IsConfidential)>(new CommandDefinition("select id, folder_id as FolderId, reg_status as RegStatus, is_confidential as IsConfidential from ged.document where tenant_id=@tenantId and id=@documentId", new { tenantId, documentId }, cancellationToken: ct));
            if (doc.Id == Guid.Empty) return Fail(documentId, "Documento não encontrado.");
            if (!string.Equals(doc.RegStatus, "A", StringComparison.OrdinalIgnoreCase)) return Fail(documentId, "Documento inativo.");
            if (doc.FolderId == destinationFolderId) return Fail(documentId, "Documento já está na pasta destino.");
            var destinationExists = await conn.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("select id from ged.folder where tenant_id=@tenantId and id=@destinationFolderId and reg_status='A' and is_active=true", new { tenantId, destinationFolderId }, cancellationToken: ct));
            if (!destinationExists.HasValue) return Fail(documentId, "Pasta destino inválida.");
            await conn.ExecuteAsync(new CommandDefinition("update ged.document set folder_id=@destinationFolderId, updated_at=now(), updated_by=@userId where tenant_id=@tenantId and id=@documentId", new { tenantId, documentId, destinationFolderId, userId }, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition("insert into ged.document_folder_move_history (id, tenant_id, document_id, old_folder_id, new_folder_id, moved_by, moved_by_name, reason, batch_id, source, reg_status) values (@id,@tenantId,@documentId,@oldFolderId,@newFolderId,@movedBy,@movedByName,@reason,@batchId,@source,'A')", new { id = Guid.NewGuid(), tenantId, documentId, oldFolderId = doc.FolderId, newFolderId = destinationFolderId, movedBy = userId, movedByName = userName, reason, batchId, source }, cancellationToken: ct));
            await _audit.WriteAsync(tenantId, userId, "UPDATE", "DOCUMENT", documentId, "Documento movido de pasta", null, null, new { oldFolderId = doc.FolderId, newFolderId = destinationFolderId, reason, source }, ct);
            return new DocumentMoveResultDto { DocumentId = documentId, Success = true, Message = "Documento movido com sucesso.", OldFolderId = doc.FolderId, NewFolderId = destinationFolderId };
        }
        catch (Exception ex)
        { _logger.LogError(ex, "Erro em MoveOneAsync Tenant={TenantId} User={UserId} Document={DocumentId} Destination={Destination}", tenantId, userId, documentId, destinationFolderId); return Fail(documentId, "Erro interno ao mover documento."); }
    }
    private static DocumentMoveResultDto Fail(Guid id, string msg) => new() { DocumentId = id, Success = false, Message = msg };
}
