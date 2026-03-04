using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Batches;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Batches;

public sealed class BatchCommands : IBatchCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<BatchCommands> _logger;

    // enum real: RECEIVED/TRIAGE/DIGITIZATION/INDEXING/ARCHIVED
    private static readonly HashSet<string> AllowedStatus = new(StringComparer.OrdinalIgnoreCase)
    {
        "RECEIVED", "TRIAGE", "DIGITIZATION", "INDEXING", "ARCHIVED"
    };

    public BatchCommands(IDbConnectionFactory db, IAuditWriter audit, ILogger<BatchCommands> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, BatchCreateVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm is null) return Result<Guid>.Fail("VM", "Dados inválidos.");
            if (string.IsNullOrWhiteSpace(vm.BatchNo)) return Result<Guid>.Fail("BATCHNO", "BatchNo é obrigatório.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string sql = @"
insert into ged.batch
(id, tenant_id, batch_no, status, created_at, created_by, notes, reg_date, reg_status)
values
(gen_random_uuid(), @tenant_id, @batch_no, 'RECEIVED'::ged.batch_status, now(), @created_by, @notes, now(), 'A')
returning id;
";
            var batchId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                batch_no = vm.BatchNo.Trim(),
                created_by = userId,
                notes = vm.Notes
            }, transaction: tx, cancellationToken: ct));

            // batch_history (Item 18: rastreio de fases)
            await InsertBatchHistoryAsync(conn, tx, tenantId, batchId, "RECEIVED", userId, "Lote criado", ct);

            if (vm.DocumentIds.Count > 0)
            {
                foreach (var docId in vm.DocumentIds.Distinct())
                {
                    await InsertBatchItemAsync(conn, tx, tenantId, batchId, docId, vm.BoxId, ct);

                    if (vm.BoxId.HasValue)
                    {
                        // Item 17/24: rastreio físico (doc entrou na caixa)
                        await InsertBoxHistoryAsync(conn, tx, tenantId, vm.BoxId.Value, docId, "ADD", userId,
                            $"Documento adicionado ao lote {vm.BatchNo} com vínculo à caixa.", ct);
                    }
                }
            }

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch", batchId,
                "Lote criado", null, null, new { vm.BatchNo, docs = vm.DocumentIds.Count, boxId = vm.BoxId }, ct);

            return Result<Guid>.Ok(batchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchCommands.CreateAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("BATCH", "Falha ao criar lote.");
        }
    }

    public async Task<Result> AddItemAsync(Guid tenantId, Guid batchId, Guid documentId, Guid? boxId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (batchId == Guid.Empty) return Result.Fail("BATCH", "Lote inválido.");
            if (documentId == Guid.Empty) return Result.Fail("DOC", "Documento inválido.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            await InsertBatchItemAsync(conn, tx, tenantId, batchId, documentId, boxId, ct);

            // se boxId veio, grava trilha física
            if (boxId.HasValue)
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, boxId.Value, documentId, "ADD", userId,
                    "Documento vinculado a caixa via lote.", ct);
            }

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch_item", batchId,
                "Item adicionado ao lote", null, null, new { documentId, boxId }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchCommands.AddItemAsync failed. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao adicionar item no lote.");
        }
    }

    public async Task<Result> MoveItemBoxAsync(Guid tenantId, Guid batchId, Guid documentId, Guid? newBoxId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (batchId == Guid.Empty) return Result.Fail("BATCH", "Lote inválido.");
            if (documentId == Guid.Empty) return Result.Fail("DOC", "Documento inválido.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // pega box atual
            const string getOld = @"
select box_id
from ged.batch_item
where tenant_id=@tenant_id and batch_id=@batch_id and document_id=@document_id and reg_status='A';
";
            var oldBoxId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(getOld, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = documentId
            }, transaction: tx, cancellationToken: ct));

            const string sql = @"
update ged.batch_item
set box_id=@box_id
where tenant_id=@tenant_id and batch_id=@batch_id and document_id=@document_id and reg_status='A';
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = documentId,
                box_id = newBoxId
            }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Item não encontrado no lote.");
            }

            // trilha física: remove da antiga (se existia) + add na nova (se existe)
            if (oldBoxId.HasValue && oldBoxId.Value != Guid.Empty && (!newBoxId.HasValue || newBoxId.Value != oldBoxId.Value))
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, oldBoxId.Value, documentId, "REMOVE", userId,
                    "Documento removido da caixa (movimentação).", ct);
            }
            if (newBoxId.HasValue && newBoxId.Value != Guid.Empty && (!oldBoxId.HasValue || newBoxId.Value != oldBoxId.Value))
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, newBoxId.Value, documentId, "ADD", userId,
                    "Documento adicionado à caixa (movimentação).", ct);
            }

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch_item", batchId,
                "Movimentação de caixa do item", null, null, new { documentId, oldBoxId, newBoxId }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchCommands.MoveItemBoxAsync failed. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao mover caixa do item.");
        }
    }

    public async Task<Result> RemoveItemAsync(Guid tenantId, Guid batchId, Guid documentId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (batchId == Guid.Empty) return Result.Fail("BATCH", "Lote inválido.");
            if (documentId == Guid.Empty) return Result.Fail("DOC", "Documento inválido.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // pega box atual antes de remover
            const string getOld = @"
select box_id
from ged.batch_item
where tenant_id=@tenant_id and batch_id=@batch_id and document_id=@document_id and reg_status='A';
";
            var oldBoxId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(getOld, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = documentId
            }, transaction: tx, cancellationToken: ct));

            const string sql = @"
update ged.batch_item
set reg_status='I'
where tenant_id=@tenant_id and batch_id=@batch_id and document_id=@document_id and reg_status='A';
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = documentId
            }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Item não encontrado no lote.");
            }

            // trilha física: se estava em caixa, registra retirada
            if (oldBoxId.HasValue && oldBoxId.Value != Guid.Empty)
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, oldBoxId.Value, documentId, "REMOVE", userId,
                    "Documento removido do lote (retirada da caixa vinculada).", ct);
            }

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch_item", batchId,
                "Item removido do lote", null, null, new { documentId, oldBoxId }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchCommands.RemoveItemAsync failed. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao remover item.");
        }
    }

    public async Task<Result> ChangeStatusAsync(Guid tenantId, Guid batchId, string newStatus, Guid? userId, string? notes, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (batchId == Guid.Empty) return Result.Fail("BATCH", "Lote inválido.");
            if (string.IsNullOrWhiteSpace(newStatus)) return Result.Fail("STATUS", "Status inválido.");

            var status = NormalizeStatus(newStatus);
            if (!AllowedStatus.Contains(status))
                return Result.Fail("STATUS", "Status inválido. Use: RECEIVED, TRIAGE, DIGITIZATION, INDEXING, ARCHIVED.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string sql = @"
update ged.batch
set status=@status::ged.batch_status,
    notes=coalesce(@notes, notes)
where tenant_id=@tenant_id and id=@batch_id and reg_status='A';
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                status,
                notes
            }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Lote não encontrado.");
            }

            // Item 18: histórico por mudança de fase
            await InsertBatchHistoryAsync(conn, tx, tenantId, batchId, status, userId, notes, ct);

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch", batchId,
                "Mudança de status do lote", null, null, new { status, notes }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchCommands.ChangeStatusAsync failed. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao alterar status do lote.");
        }
    }

    // ---------------- helpers ----------------

    private static string NormalizeStatus(string s)
    {
        var x = (s ?? "").Trim().ToUpperInvariant();
        return x switch
        {
            "RECEBIDO" => "RECEIVED",
            "TRIAGEM" => "TRIAGE",
            "DIGITALIZACAO" or "DIGITALIZAÇÃO" or "DIGITIZACAO" => "DIGITIZATION",
            "INDEXACAO" or "INDEXAÇÃO" => "INDEXING",
            "ARQUIVADO" or "ARQUIVAMENTO" => "ARCHIVED",
            _ => x
        };
    }

    private static async Task InsertBatchItemAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid tenantId,
        Guid batchId,
        Guid documentId,
        Guid? boxId,
        CancellationToken ct)
    {
        const string ins = @"
insert into ged.batch_item(tenant_id, batch_id, document_id, box_id, reg_date, reg_status)
values(@tenant_id, @batch_id, @document_id, @box_id, now(), 'A')
on conflict do nothing;
";
        await conn.ExecuteAsync(new CommandDefinition(ins, new
        {
            tenant_id = tenantId,
            batch_id = batchId,
            document_id = documentId,
            box_id = boxId
        }, transaction: tx, cancellationToken: ct));
    }

    private static async Task InsertBatchHistoryAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid tenantId,
        Guid batchId,
        string status,
        Guid? userId,
        string? notes,
        CancellationToken ct)
    {
        // ajuste se sua tabela tiver nomes diferentes
        const string hist = @"
insert into ged.batch_history
(tenant_id, batch_id, event_time, event_type, by_user_id, notes, reg_date, reg_status)
values
(@tenant_id, @batch_id, now(), @event_type, @by_user_id, @notes, now(), 'A');
";
        await conn.ExecuteAsync(new CommandDefinition(hist, new
        {
            tenant_id = tenantId,
            batch_id = batchId,
            event_type = status,
            by_user_id = userId,
            notes
        }, transaction: tx, cancellationToken: ct));
    }

    private static async Task InsertBoxHistoryAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid tenantId,
        Guid boxId,
        Guid documentId,
        string eventType,
        Guid? userId,
        string? notes,
        CancellationToken ct)
    {
        // ajuste se sua tabela tiver nomes diferentes
        const string hist = @"
insert into ged.box_content_history
(tenant_id, box_id, document_id, event_time, event_type, by_user_id, notes, reg_date, reg_status)
values
(@tenant_id, @box_id, @document_id, now(), @event_type, @by_user_id, @notes, now(), 'A');
";
        await conn.ExecuteAsync(new CommandDefinition(hist, new
        {
            tenant_id = tenantId,
            box_id = boxId,
            document_id = documentId,
            event_type = eventType,
            by_user_id = userId,
            notes
        }, transaction: tx, cancellationToken: ct));
    }
}