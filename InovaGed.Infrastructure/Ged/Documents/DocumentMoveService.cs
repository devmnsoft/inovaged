using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Folders;
using InovaGed.Infrastructure.Security;
using InovaGed.Application.Security;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class DocumentMoveService : IDocumentMoveService
{
    private static readonly Guid VirtualRootFolderId = Guid.Parse("f0000000-0000-0000-0000-000000000001");
    private const int BulkLimit = 200;
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<DocumentMoveService> _logger;
    private readonly IPermissionService _permissionService;
    private readonly IAbacAuthorizationService _authorization;

    public DocumentMoveService(IDbConnectionFactory db, IAuditWriter audit, ILogger<DocumentMoveService> logger, IPermissionService permissionService, IAbacAuthorizationService authorization)
    { _db = db; _audit = audit; _logger = logger; _permissionService = permissionService; _authorization = authorization; }

    public Task<Result<DocumentMoveResultDto>> MoveAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, bool isAdmin, CancellationToken ct)
        => MoveAsync(tenantId, userId, userName, documentId, destinationFolderId, reason, source, isAdmin, null, null, ct);

    public async Task<Result<DocumentMoveResultDto>> MoveAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, bool isAdmin, string? correlationId, string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            destinationFolderId = await ResolveDestinationAsync(conn, tenantId, destinationFolderId, ct);
            if (destinationFolderId == Guid.Empty) return Result<DocumentMoveResultDto>.Fail("FOLDER_NOT_FOUND", "Pasta de destino não encontrada ou sem mapeamento real.");
            var batchId = BatchIdFrom(tenantId, idempotencyKey);
            var conflict = await ReadIdempotencyConflictAsync(conn, null, tenantId, batchId, destinationFolderId, ct);
            if (conflict is not null)
                return Result<DocumentMoveResultDto>.Fail("MOVE_FAILED", conflict);

            var item = await MoveOneCoreAsync(conn, null, tenantId, userId, userName, documentId, destinationFolderId, reason, source, isAdmin, batchId, correlationId, ct);
            return item.Success
                ? Result<DocumentMoveResultDto>.Ok(item)
                : Result<DocumentMoveResultDto>.Fail(item.ErrorCode ?? "MOVE_FAILED", item.Message ?? "Não foi possível concluir a movimentação.");
        }
        catch (Exception ex)
        {
            LogMoveFailure(ex, "MoveAsync", tenantId, userId, destinationFolderId, correlationId, documentId);
            return Result<DocumentMoveResultDto>.Fail(CodeFor(ex), "Não foi possível concluir a movimentação do documento no momento.");
        }
    }

    public Task<Result<DocumentBulkMoveResultDto>> MoveBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, bool isAdmin, CancellationToken ct)
        => MoveBulkAsync(tenantId, userId, userName, documentIds, destinationFolderId, reason, source, isAdmin, false, null, null, ct);

    public async Task<Result<DocumentBulkMoveResultDto>> MoveBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, bool isAdmin, bool allOrNothing, string? idempotencyKey, string? correlationId, CancellationToken ct)
    {
        try
        {
            if (documentIds.Count == 0) return Result<DocumentBulkMoveResultDto>.Fail("VALIDATION", "Nenhum documento selecionado.");
            var distinct = documentIds.Where(id => id != Guid.Empty).Distinct().ToArray();
            if (distinct.Length == 0) return Result<DocumentBulkMoveResultDto>.Fail("VALIDATION", "Nenhum documento selecionado.");
            if (distinct.Length > BulkLimit) return Result<DocumentBulkMoveResultDto>.Fail("LIMIT", $"Limite máximo de {BulkLimit} documentos por operação.");

            var batchId = BatchIdFrom(tenantId, idempotencyKey);
            await using var conn = await _db.OpenAsync(ct);
            destinationFolderId = await ResolveDestinationAsync(conn, tenantId, destinationFolderId, ct);
            if (destinationFolderId == Guid.Empty) return Result<DocumentBulkMoveResultDto>.Fail("FOLDER_NOT_FOUND", "Pasta de destino não encontrada ou sem mapeamento real.");
            var conflict = await ReadIdempotencyConflictAsync(conn, null, tenantId, batchId, destinationFolderId, ct);
            if (conflict is not null)
                return Result<DocumentBulkMoveResultDto>.Fail("MOVE_FAILED", conflict);

            var replay = await ReadFullReplayAsync(conn, null, tenantId, batchId, destinationFolderId, distinct, ct);
            if (replay is not null && (await _authorization.FilterDocumentsAsync(tenantId, userId, distinct, "EDIT", ct)).Count == distinct.Length)
            {
                replay.AllOrNothing = allOrNothing;
                return Result<DocumentBulkMoveResultDto>.Ok(replay);
            }

            var outcome = allOrNothing
                ? await MoveAtomicAsync(conn, tenantId, userId, userName, distinct, destinationFolderId, reason, source, isAdmin, batchId, correlationId, ct)
                : await MovePartialAsync(conn, tenantId, userId, userName, distinct, destinationFolderId, reason, source, isAdmin, batchId, correlationId, ct);

            if (outcome.SuccessCount > 0)
            {
                await WriteAuditSafelyAsync(tenantId, userId, "MOVE_DOCUMENT_FOLDER_BULK", batchId,
                    "Documentos movidos em lote", new { batchId, correlationId, allOrNothing, outcome.Total, outcome.SuccessCount, outcome.FailCount, outcome.RolledBack, destinationFolderId, source }, ct);
            }

            return Result<DocumentBulkMoveResultDto>.Ok(outcome);
        }
        catch (Exception ex)
        {
            LogMoveFailure(ex, "MoveBulkAsync", tenantId, userId, destinationFolderId, correlationId, null);
            return Result<DocumentBulkMoveResultDto>.Fail(CodeFor(ex), "Não foi possível concluir a movimentação dos documentos.");
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
select id as Id, name as Name, full_path as FullPath, parent_id as ParentId, true as CanWrite
from folder_tree
where cast(@term as text) is null
   or name ilike ('%' || cast(@term as text) || '%')
   or full_path ilike ('%' || cast(@term as text) || '%')
order by full_path
limit 30;
""";

            var folders = (await conn.QueryAsync<FolderOptionDto>(
                new CommandDefinition(sql, new { tenantId, term = normalizedTerm }, cancellationToken: ct))).AsList();
            var canMove = await CanMoveAsync(conn, null, tenantId, userId, false, ct);
            foreach (var folder in folders) folder.CanWrite = canMove;
            return folders;
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
select h.id as Id, h.document_id as DocumentId, h.old_folder_id as OldFolderId, h.new_folder_id as NewFolderId,
       coalesce(fo.name, case when h.old_folder_id is null then 'Raiz' else null end) as OldFolderName,
       coalesce(fn.name, case when h.new_folder_id is null then 'Raiz' else null end) as NewFolderName,
       h.moved_by as MovedBy, h.moved_by_name as MovedByName, h.moved_at as MovedAt, h.reason as Reason,
       h.source as Source, h.batch_id as BatchId, h.correlation_id as CorrelationId
from ged.document_folder_move_history h
left join ged.folder fo on fo.tenant_id = h.tenant_id and fo.id = h.old_folder_id
left join ged.folder fn on fn.tenant_id = h.tenant_id and fn.id = h.new_folder_id
where h.tenant_id = @tenantId and h.document_id = @documentId and h.reg_status = 'A'
order by h.moved_at desc;
""";
            return (await conn.QueryAsync<DocumentMoveHistoryDto>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct))).AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em GetMoveHistoryAsync Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            return Array.Empty<DocumentMoveHistoryDto>();
        }
    }

    public async Task<Result<DocumentMoveResultDto>> UndoAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid? historyId, string? reason, bool isAdmin, string? correlationId, string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            if (documentId == Guid.Empty)
                return Result<DocumentMoveResultDto>.Fail("VALIDATION", "Documento inválido.");
            var item = await UndoOneAsync(tenantId, userId, userName, documentId, historyId, reason, isAdmin, correlationId, idempotencyKey, ct);
            return item.Success
                ? Result<DocumentMoveResultDto>.Ok(item)
                : Result<DocumentMoveResultDto>.Fail(item.ErrorCode ?? "MOVE_FAILED", item.Message ?? "Não foi possível desfazer a movimentação.");
        }
        catch (Exception ex)
        {
            LogMoveFailure(ex, "UndoAsync", tenantId, userId, Guid.Empty, correlationId, documentId);
            return Result<DocumentMoveResultDto>.Fail(CodeFor(ex), "Não foi possível desfazer a movimentação do documento no momento.");
        }
    }

    public async Task<Result<DocumentBulkMoveResultDto>> UndoBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<DocumentUndoItemVM> items, string? reason, bool isAdmin, string? correlationId, string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            if (items.Count == 0) return Result<DocumentBulkMoveResultDto>.Fail("VALIDATION", "Nenhum documento selecionado.");
            var distinct = items.Where(item => item.DocumentId != Guid.Empty).GroupBy(item => item.DocumentId).Select(group => group.First()).ToArray();
            if (distinct.Length == 0) return Result<DocumentBulkMoveResultDto>.Fail("VALIDATION", "Nenhum documento selecionado.");
            if (distinct.Length > BulkLimit) return Result<DocumentBulkMoveResultDto>.Fail("LIMIT", $"Limite máximo de {BulkLimit} documentos por operação.");

            var results = new List<DocumentMoveResultDto>(distinct.Length);
            foreach (var item in distinct)
            {
                var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim() + ":" + item.DocumentId.ToString("N");
                try
                {
                    results.Add(await UndoOneAsync(tenantId, userId, userName, item.DocumentId, item.HistoryId, reason, isAdmin, correlationId, key, ct));
                }
                catch (Exception ex)
                {
                    LogMoveFailure(ex, "UndoBulkAsync", tenantId, userId, Guid.Empty, correlationId, item.DocumentId);
                    results.Add(Fail(item.DocumentId, CodeFor(ex), "Não foi possível desfazer a movimentação deste documento."));
                }
            }

            return Result<DocumentBulkMoveResultDto>.Ok(Summarize(Guid.NewGuid(), results, allOrNothing: false, rolledBack: false, replay: false));
        }
        catch (Exception ex)
        {
            LogMoveFailure(ex, "UndoBulkAsync", tenantId, userId, Guid.Empty, correlationId, null);
            return Result<DocumentBulkMoveResultDto>.Fail(CodeFor(ex), "Não foi possível desfazer a movimentação dos documentos.");
        }
    }

    private async Task<DocumentMoveResultDto> UndoOneAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid? historyId, string? reason, bool isAdmin, string? correlationId, string? idempotencyKey, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var history = await conn.QueryFirstOrDefaultAsync<MoveHistoryProbe>(new CommandDefinition("""
select h.id as "Id", h.old_folder_id as "OldFolderId", h.new_folder_id as "NewFolderId"
from ged.document_folder_move_history h
where h.tenant_id = @tenantId
  and h.document_id = @documentId
  and h.reg_status = 'A'
  and (cast(@historyId as uuid) is null or h.id = cast(@historyId as uuid))
order by h.moved_at desc
limit 1
""", new { tenantId, documentId, historyId }, cancellationToken: ct));
        if (history is null || history.Id == Guid.Empty)
            return Fail(documentId, "HISTORY_NOT_FOUND", "Não há movimentação recente deste documento para desfazer.");

        var destination = history.OldFolderId ?? VirtualRootFolderId;
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? "undo:" + history.Id.ToString("N") : idempotencyKey.Trim();
        var batchId = BatchIdFrom(tenantId, key);
        var conflict = await ReadIdempotencyConflictAsync(conn, null, tenantId, batchId, destination, ct);
        if (conflict is not null)
            return Fail(documentId, "MOVE_FAILED", conflict);

        var item = await MoveOneCoreAsync(conn, null, tenantId, userId, userName, documentId, destination, string.IsNullOrWhiteSpace(reason) ? "Reversão da movimentação" : reason.Trim(), "UNDO", isAdmin, batchId, correlationId, ct, history.NewFolderId, enforceExpectedFolder: true, respectRetentionHold: true);
        if (item.Success && item.ErrorCode == "ALREADY_IN_DESTINATION")
            item.Message = "Esta reversão já tinha sido aplicada.";
        return item;
    }

    private async Task<DocumentBulkMoveResultDto> MovePartialAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, bool isAdmin, Guid batchId, string? correlationId, CancellationToken ct)
    {
        var items = new List<DocumentMoveResultDto>(documentIds.Count);
        foreach (var id in documentIds)
            items.Add(await MoveOneCoreAsync(conn, null, tenantId, userId, userName, id, destinationFolderId, reason, source, isAdmin, batchId, correlationId, ct));
        return Summarize(batchId, items, allOrNothing: false, rolledBack: false, replay: false);
    }

    private async Task<DocumentBulkMoveResultDto> MoveAtomicAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, bool isAdmin, Guid batchId, string? correlationId, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        var applied = new List<DocumentMoveResultDto>(documentIds.Count);
        foreach (var id in documentIds)
        {
            var item = await MoveOneCoreAsync(conn, tx, tenantId, userId, userName, id, destinationFolderId, reason, source, isAdmin, batchId, correlationId, ct);
            if (!item.Success)
            {
                await tx.RollbackAsync(ct);
                return RollBackBatch(batchId, documentIds, applied, item);
            }
            applied.Add(item);
        }

        await tx.CommitAsync(ct);
        return Summarize(batchId, applied, allOrNothing: true, rolledBack: false, replay: false);
    }

    private async Task<DocumentMoveResultDto> MoveOneCoreAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, bool isAdmin, Guid? batchId, string? correlationId, CancellationToken ct, Guid? expectedFolderId = null, bool enforceExpectedFolder = false, bool respectRetentionHold = false)
    {
        var ownsTransaction = tx is null;
        DbTransaction? localTx = tx;
        NpgsqlTransaction? ownedTx = null;
        var committed = false;
        Guid? targetFolderId = destinationFolderId == VirtualRootFolderId ? null : destinationFolderId;
        try
        {
            if (ownsTransaction)
            {
                ownedTx = await conn.BeginTransactionAsync(ct);
                localTx = ownedTx;
            }

            if (!await _authorization.CanAccessDocumentAsync(tenantId, userId, documentId, "EDIT", new Dictionary<string, string>(), ct))
                return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "ACCESS_DENIED", "Você não tem permissão para mover este documento."), ct);

            var replay = await ReadDocumentReplayAsync(conn, localTx, tenantId, documentId, batchId, destinationFolderId, ct);
            if (replay is not null)
            {
                if (enforceExpectedFolder)
                {
                    var current = await LockedFolderAsync(conn, localTx, tenantId, documentId, ct);
                    if (current is null)
                        return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "DOCUMENT_NOT_FOUND", "Documento não encontrado."), ct);
                    if (current.FolderId != targetFolderId)
                        return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "CONCURRENT_MODIFICATION", "A reversão já tinha sido registrada, mas o documento foi movido novamente."), ct);
                }

                if (ownsTransaction) await localTx!.RollbackAsync(ct);
                return replay;
            }

            const string documentSql = """
select
    d.id AS "Id",
    d.folder_id AS "FolderId",
    d.status::text AS "Status",
    (upper(coalesce(d.visibility::text, '')) in ('PRIVATE', 'RESTRICTED', 'CONFIDENTIAL', 'SIGILOSO', 'RESTRITO')
      or upper(coalesce(d.security_level::text, '')) not in ('', 'PUBLIC', 'NORMAL', 'LOW', '0')) AS "IsConfidential"
from ged.document d
where d.tenant_id = @tenantId
  and d.id = @documentId
  and upper(d.status::text) <> 'DELETED'
for update;
""";

            var doc = await conn.QueryFirstOrDefaultAsync<DocumentMoveSnapshot>(
                new CommandDefinition(documentSql, new { tenantId, documentId }, transaction: localTx, cancellationToken: ct));
            if (doc is null || doc.Id == Guid.Empty)
                return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "DOCUMENT_NOT_FOUND", "Documento não encontrado."), ct);

            if (enforceExpectedFolder && doc.FolderId != expectedFolderId)
                return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "CONCURRENT_MODIFICATION", "O documento não está mais na pasta desta movimentação. A reversão não foi aplicada."), ct);

            if (doc.FolderId == targetFolderId)
                return await FinishAsync(ownsTransaction, localTx, Already(documentId, doc.FolderId, targetFolderId), ct);

            if (respectRetentionHold && await IsRetentionHeldAsync(conn, localTx, tenantId, documentId, ct))
                return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "DOCUMENT_LOCKED", "O documento está bloqueado por temporalidade e a reversão não foi aplicada."), ct);

            if (!isAdmin && !await CanMoveAsync(conn, localTx, tenantId, userId, doc.IsConfidential, ct))
            {
                await WriteAuditSafelyAsync(tenantId, userId, "ACCESS_DENIED_MOVE_DOCUMENT", documentId,
                    "Tentativa não autorizada de mover documento", new { destinationFolderId, source, correlationId, confidential = doc.IsConfidential }, ct);
                return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "ACCESS_DENIED", "Você não tem permissão para mover este documento."), ct);
            }

            if (targetFolderId.HasValue)
            {
                var destination = await conn.QueryFirstOrDefaultAsync<DestinationFolderSnapshot>(new CommandDefinition("""
select id as "Id", is_active as "IsActive", reg_status as "RegStatus"
from ged.folder
where tenant_id = @tenantId
  and id = cast(@destinationFolderId as uuid)
for share
""", new { tenantId, destinationFolderId = targetFolderId }, transaction: localTx, cancellationToken: ct));
                if (destination is null)
                    return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "FOLDER_NOT_FOUND", "Pasta de destino não encontrada."), ct);
                if (!destination.IsActive || !string.Equals(destination.RegStatus, "A", StringComparison.OrdinalIgnoreCase))
                    return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "FOLDER_INACTIVE", "A pasta de destino está inativa."), ct);
            }

            var rowsAffected = await conn.ExecuteAsync(new CommandDefinition("""
update ged.document
set folder_id = cast(@destinationFolderId as uuid),
    updated_at = now(),
    updated_by = @userId
where tenant_id = @tenantId
  and id = @documentId
  and upper(status::text) <> 'DELETED'
""", new { tenantId, documentId, destinationFolderId = targetFolderId, userId }, transaction: localTx, cancellationToken: ct));
            if (rowsAffected != 1)
                return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "CONCURRENT_MODIFICATION", "O documento foi alterado por outra operação. Atualize a lista e tente novamente."), ct);

            var historyId = Guid.NewGuid();
            var undoing = string.Equals(source, "UNDO", StringComparison.OrdinalIgnoreCase);
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.document_folder_move_history
    (id, tenant_id, document_id, old_folder_id, new_folder_id, moved_by, moved_by_name, reason, batch_id, source, correlation_id, reg_status)
values
    (@id, @tenantId, @documentId, cast(@oldFolderId as uuid), cast(@newFolderId as uuid), @movedBy, @movedByName, @reason, cast(@batchId as uuid), @source, @correlationId, 'A')
""", new
            {
                id = historyId,
                tenantId,
                documentId,
                oldFolderId = doc.FolderId,
                newFolderId = targetFolderId,
                movedBy = userId,
                movedByName = userName,
                reason,
                batchId,
                source,
                correlationId
            }, transaction: localTx, cancellationToken: ct));

            var persisted = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
select count(1)
from ged.document
where tenant_id = @tenantId
  and id = @documentId
  and folder_id is not distinct from cast(@destinationFolderId as uuid)
""", new { tenantId, documentId, destinationFolderId = targetFolderId }, transaction: localTx, cancellationToken: ct));
            if (persisted != 1)
                return await FinishAsync(ownsTransaction, localTx, Fail(documentId, "MOVE_FAILED", "Não foi possível confirmar a pasta destino do documento."), ct);

            if (ownsTransaction)
            {
                await localTx!.CommitAsync(ct);
                committed = true;
                await WriteAuditSafelyAsync(tenantId, userId, undoing ? "UNDO_DOCUMENT_FOLDER" : "MOVE_DOCUMENT_FOLDER", documentId,
                    undoing ? "Movimentação de pasta desfeita" : "Documento movido de pasta", new { oldFolderId = doc.FolderId, newFolderId = targetFolderId, source, batchId, correlationId, historyId }, ct);
            }

            _logger.LogInformation("Documento movido confirmado. Tenant={TenantId} User={UserId} Document={DocumentId} OldFolder={OldFolderId} NewFolder={DestinationFolderId} CorrelationId={CorrelationId}", tenantId, userId, documentId, doc.FolderId, targetFolderId, correlationId);
            return new DocumentMoveResultDto
            {
                DocumentId = documentId,
                Success = true,
                Message = undoing ? "Movimentação desfeita. O documento voltou para a pasta anterior." : "Documento movido com sucesso.",
                OldFolderId = doc.FolderId,
                NewFolderId = targetFolderId,
                HistoryId = historyId
            };
        }
        catch (Exception ex) when (ex is PostgresException || ex.InnerException is PostgresException)
        {
            var pg = FindPostgres(ex)!;
            if (pg.SqlState == "23505" && await DestinationMatchesAsync(conn, localTx, tenantId, documentId, targetFolderId, ct))
            {
                if (ownsTransaction && !committed && localTx is not null)
                    await SafeRollbackAsync(localTx, ct);
                return new DocumentMoveResultDto { DocumentId = documentId, Success = true, Message = "Movimentação já confirmada para este destino.", ErrorCode = "ALREADY_IN_DESTINATION", NewFolderId = targetFolderId };
            }

            if (committed)
            {
                _logger.LogError(ex, "Falha posterior ao commit. Movimento permanece confirmado. Tenant={TenantId} Document={DocumentId} SqlState={SqlState} CorrelationId={CorrelationId}", tenantId, documentId, pg.SqlState, correlationId);
                return new DocumentMoveResultDto { DocumentId = documentId, Success = true, Message = "Documento movido com sucesso; auditoria pendente de recuperação.", NewFolderId = targetFolderId };
            }

            if (ownsTransaction && localTx is not null)
                await SafeRollbackAsync(localTx, ct);
            LogPostgres(pg, tenantId, userId, documentId, destinationFolderId, correlationId);
            return Fail(documentId, CodeFor(pg), MessageFor(pg));
        }
        catch (Exception ex)
        {
            if (committed)
            {
                _logger.LogError(ex, "Falha posterior ao commit. Movimento permanece confirmado. Tenant={TenantId} Document={DocumentId} CorrelationId={CorrelationId}", tenantId, documentId, correlationId);
                return new DocumentMoveResultDto { DocumentId = documentId, Success = true, Message = "Documento movido com sucesso; auditoria pendente de recuperação.", NewFolderId = targetFolderId };
            }

            if (ownsTransaction && localTx is not null)
                await SafeRollbackAsync(localTx, ct);
            LogMoveFailure(ex, "MoveOneCoreAsync", tenantId, userId, destinationFolderId, correlationId, documentId);
            if (!ownsTransaction && IsAbortedTransaction(ex))
                throw;
            return Fail(documentId, CodeFor(ex), "Não foi possível concluir a movimentação do documento. Tente novamente.");
        }
        finally
        {
            if (ownedTx is not null)
                await ownedTx.DisposeAsync();
        }
    }

    private async Task<string?> ReadIdempotencyConflictAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid? batchId, Guid destinationFolderId, CancellationToken ct)
    {
        if (!batchId.HasValue) return null;
        var target = destinationFolderId == VirtualRootFolderId ? (Guid?)null : destinationFolderId;
        var conflict = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
select count(1)
from ged.document_folder_move_history
where tenant_id = @tenantId
  and batch_id is not distinct from cast(@batchId as uuid)
  and reg_status = 'A'
  and new_folder_id is distinct from cast(@destinationFolderId as uuid)
""", new { tenantId, batchId, destinationFolderId = target }, transaction: tx, cancellationToken: ct));
        return conflict > 0 ? "Esta solicitação já foi usada para outro destino." : null;
    }

    private async Task<DocumentBulkMoveResultDto?> ReadFullReplayAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid? batchId, Guid destinationFolderId, IReadOnlyList<Guid> documentIds, CancellationToken ct)
    {
        if (!batchId.HasValue || documentIds.Count == 0) return null;
        var target = destinationFolderId == VirtualRootFolderId ? (Guid?)null : destinationFolderId;
        var rows = (await conn.QueryAsync<Guid>(new CommandDefinition("""
select document_id
from ged.document_folder_move_history
where tenant_id = @tenantId
  and batch_id is not distinct from cast(@batchId as uuid)
  and reg_status = 'A'
  and new_folder_id is not distinct from cast(@destinationFolderId as uuid)
""", new { tenantId, batchId, destinationFolderId = target }, transaction: tx, cancellationToken: ct))).ToHashSet();
        if (!documentIds.All(rows.Contains)) return null;
        var items = documentIds.Select(id => new DocumentMoveResultDto
        {
            DocumentId = id,
            Success = true,
            ErrorCode = "ALREADY_IN_DESTINATION",
            Message = "Movimentação já confirmada para este destino.",
            NewFolderId = target
        }).ToArray();
        var summary = Summarize(batchId.Value, items, false, false, replay: true);
        return summary;
    }

    private async Task<DocumentMoveResultDto?> ReadDocumentReplayAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid documentId, Guid? batchId, Guid destinationFolderId, CancellationToken ct)
    {
        if (!batchId.HasValue) return null;
        var target = destinationFolderId == VirtualRootFolderId ? (Guid?)null : destinationFolderId;
        var found = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
select count(1)
from ged.document_folder_move_history
where tenant_id = @tenantId
  and document_id = @documentId
  and batch_id is not distinct from cast(@batchId as uuid)
  and reg_status = 'A'
  and new_folder_id is not distinct from cast(@destinationFolderId as uuid)
""", new { tenantId, documentId, batchId, destinationFolderId = target }, transaction: tx, cancellationToken: ct));
        if (found < 1) return null;
        return new DocumentMoveResultDto
        {
            DocumentId = documentId,
            Success = true,
            ErrorCode = "ALREADY_IN_DESTINATION",
            Message = "Movimentação já confirmada para este destino.",
            NewFolderId = target
        };
    }

    private async Task<bool> DestinationMatchesAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid documentId, Guid? targetFolderId, CancellationToken ct)
    {
        try
        {
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
select count(1)
from ged.document
where tenant_id = @tenantId
  and id = @documentId
  and folder_id is not distinct from cast(@destinationFolderId as uuid)
""", new { tenantId, documentId, destinationFolderId = targetFolderId }, transaction: tx, cancellationToken: ct)) == 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível confirmar destino após conflito de histórico. Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            return false;
        }
    }

    private async Task WriteAuditSafelyAsync(Guid tenantId, Guid userId, string action, Guid entityId, string description, object details, CancellationToken ct)
    {
        try
        {
            await _audit.WriteAsync(tenantId, userId, action, action.Contains("BULK", StringComparison.Ordinal) ? "DOCUMENT_BATCH" : "DOCUMENT",
                entityId, description, null, null, details, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AUDIT_RECOVERY_REQUIRED Tenant={TenantId} User={UserId} Action={Action} Entity={EntityId}", tenantId, userId, action, entityId);
        }
    }

    private static async Task<FolderProbe?> LockedFolderAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid documentId, CancellationToken ct)
        => await conn.QueryFirstOrDefaultAsync<FolderProbe>(new CommandDefinition("""
select folder_id as "FolderId"
from ged.document
where tenant_id = @tenantId
  and id = @documentId
  and upper(status::text) <> 'DELETED'
for update
""", new { tenantId, documentId }, transaction: tx, cancellationToken: ct));

    private static async Task<bool> IsRetentionHeldAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid documentId, CancellationToken ct)
    {
        var hasColumn = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1 from information_schema.columns
    where table_schema = 'ged' and table_name = 'document' and column_name = 'retention_hold')
""", transaction: tx, cancellationToken: ct));
        if (hasColumn)
        {
            var held = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select coalesce(retention_hold, false)
from ged.document
where tenant_id = @tenantId and id = @documentId
""", new { tenantId, documentId }, transaction: tx, cancellationToken: ct));
            if (held) return true;
        }

        var hasTable = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1 from information_schema.tables
    where table_schema = 'ged' and table_name = 'retention_hold')
""", transaction: tx, cancellationToken: ct));
        if (!hasTable) return false;
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1 from ged.retention_hold
    where tenant_id = @tenantId and document_id = @documentId and is_active = true)
""", new { tenantId, documentId }, transaction: tx, cancellationToken: ct));
    }

    private async Task<bool> CanMoveAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid userId, bool isConfidential, CancellationToken ct)
    {
        var roles = await GetUserRolesAsync(conn, tx, tenantId, userId, ct);
        if (roles.Contains("ADMIN") || roles.Contains("ADMINISTRADOR")) return true;
        if (roles.Contains("ADMINISTRADOROPHIR") || roles.Contains("ARQUIVISTAOPHIR"))
            return !isConfidential || await _permissionService.HasAsync(tenantId, userId, "GED_DOCUMENT_MOVE_CONFIDENTIAL", ct);
        return await _permissionService.HasAsync(tenantId, userId, "GED_DOCUMENT_MOVE", ct)
            && (!isConfidential || await _permissionService.HasAsync(tenantId, userId, "GED_DOCUMENT_MOVE_CONFIDENTIAL", ct));
    }

    private static Task<HashSet<string>> GetUserRolesAsync(NpgsqlConnection conn, DbTransaction? tx, Guid tenantId, Guid userId, CancellationToken ct)
        => UserRoleReader.ReadAsync(conn, tx, tenantId, userId, ct);

    private static async Task<Guid> ResolveDestinationAsync(NpgsqlConnection conn, Guid tenantId, Guid destination, CancellationToken ct)
    {
        if (!FolderIdHelper.IsVirtualFolder(destination)) return destination;
        var hasMap = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.folder_virtual_map') is not null", cancellationToken: ct));
        if (hasMap)
        {
            var mapped = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
select f.id from ged.folder_virtual_map m
join ged.folder f on f.tenant_id=m.tenant_id and f.id=m.real_folder_id
where m.tenant_id=@tenantId and m.virtual_folder_id=@destination and m.reg_status='A'
and f.is_active and f.reg_status='A' limit 1
""", new { tenantId, destination }, cancellationToken: ct));
            if (mapped.HasValue) return mapped.Value;
        }
        var real = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.folder where tenant_id=@tenantId and id=@destination and is_active and reg_status='A')", new { tenantId, destination }, cancellationToken: ct));
        return real || destination == VirtualRootFolderId ? destination : Guid.Empty;
    }

    private static async Task<DocumentMoveResultDto> FinishAsync(bool ownsTransaction, DbTransaction? tx, DocumentMoveResultDto result, CancellationToken ct)
    {
        if (ownsTransaction && tx is not null)
            await tx.RollbackAsync(ct);
        return result;
    }

    private static async Task SafeRollbackAsync(DbTransaction tx, CancellationToken ct)
    {
        try { await tx.RollbackAsync(ct); } catch (Exception) { /* a transação já pode estar abortada */ }
    }

    private static DocumentBulkMoveResultDto Summarize(Guid batchId, IReadOnlyList<DocumentMoveResultDto> items, bool allOrNothing, bool rolledBack, bool replay)
    {
        var ok = items.Count(i => i.Success);
        return new DocumentBulkMoveResultDto
        {
            BatchId = batchId,
            Total = items.Count,
            SuccessCount = ok,
            FailCount = items.Count - ok,
            AllOrNothing = allOrNothing,
            RolledBack = rolledBack,
            IdempotentReplay = replay,
            Items = items
        };
    }

    private static DocumentBulkMoveResultDto RollBackBatch(Guid batchId, IReadOnlyList<Guid> documentIds, IReadOnlyList<DocumentMoveResultDto> applied, DocumentMoveResultDto failed)
    {
        var items = new List<DocumentMoveResultDto>(documentIds.Count);
        foreach (var previous in applied)
        {
            items.Add(new DocumentMoveResultDto
            {
                DocumentId = previous.DocumentId,
                Success = false,
                ErrorCode = "MOVE_FAILED",
                Message = "Nenhuma alteração foi confirmada porque outro documento do lote falhou.",
                OldFolderId = previous.OldFolderId,
                NewFolderId = previous.OldFolderId
            });
        }

        items.Add(failed);
        foreach (var id in documentIds)
        {
            if (items.Any(item => item.DocumentId == id)) continue;
            items.Add(Fail(id, "MOVE_FAILED", "Documento não processado porque o lote foi revertido."));
        }

        return Summarize(batchId, items, allOrNothing: true, rolledBack: true, replay: false);
    }

    private static Guid BatchIdFrom(Guid tenantId, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return Guid.NewGuid();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tenantId.ToString("N") + "|" + idempotencyKey.Trim()));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static DocumentMoveResultDto Fail(Guid id, string code, string message) => new() { DocumentId = id, Success = false, Message = message, ErrorCode = code };

    private static DocumentMoveResultDto Already(Guid id, Guid? folderId, Guid? target) => new()
    {
        DocumentId = id,
        Success = true,
        ErrorCode = "ALREADY_IN_DESTINATION",
        Message = "O documento já está na pasta de destino.",
        OldFolderId = folderId,
        NewFolderId = target
    };

    private static PostgresException? FindPostgres(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg) return pg;
        }
        return null;
    }

    private static bool IsAbortedTransaction(Exception ex) => FindPostgres(ex)?.SqlState == "25P02";

    private static string CodeFor(Exception ex) => CodeFor(FindPostgres(ex));

    private static string CodeFor(PostgresException? pg) => pg?.SqlState switch
    {
        "23502" or "23503" or "23505" or "23514" => "DATABASE_CONSTRAINT_ERROR",
        "40001" or "40P01" => "CONCURRENT_MODIFICATION",
        _ => "MOVE_FAILED"
    };

    private static string MessageFor(PostgresException pg) => pg.SqlState switch
    {
        "23502" or "23503" or "23505" or "23514" => "A movimentação foi recusada por uma restrição do arquivo.",
        "40001" or "40P01" => "O documento foi alterado por outra operação. Atualize a lista e tente novamente.",
        _ => "Não foi possível concluir a movimentação do documento. Tente novamente."
    };

    private void LogPostgres(PostgresException pg, Guid tenantId, Guid userId, Guid documentId, Guid destinationFolderId, string? correlationId)
    {
        _logger.LogError("Falha PostgreSQL ao mover documento. Tenant={TenantId} User={UserId} Document={DocumentId} Destination={DestinationFolderId} SqlState={SqlState} Constraint={Constraint} Table={Table} Message={PgMessage} CorrelationId={CorrelationId}",
            tenantId, userId, documentId, destinationFolderId, pg.SqlState, pg.ConstraintName, pg.TableName, pg.MessageText, correlationId);
    }

    private void LogMoveFailure(Exception ex, string operation, Guid tenantId, Guid userId, Guid destinationFolderId, string? correlationId, Guid? documentId)
    {
        var pg = FindPostgres(ex);
        _logger.LogError(ex, "Erro em {Operation}. Tenant={TenantId} User={UserId} Document={DocumentId} Destination={DestinationFolderId} SqlState={SqlState} CorrelationId={CorrelationId}",
            operation, tenantId, userId, documentId, destinationFolderId, pg?.SqlState, correlationId);
    }

    private sealed class DocumentMoveSnapshot
    {
        public Guid Id { get; set; }
        public Guid? FolderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsConfidential { get; set; }
    }

    private sealed class DestinationFolderSnapshot
    {
        public Guid Id { get; set; }
        public bool IsActive { get; set; }
        public string RegStatus { get; set; } = string.Empty;
    }

    private sealed class FolderProbe
    {
        public Guid? FolderId { get; set; }
    }

    private sealed class MoveHistoryProbe
    {
        public Guid Id { get; set; }
        public Guid? OldFolderId { get; set; }
        public Guid? NewFolderId { get; set; }
    }
}
