using System.Data;
using System.Text.Json;
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

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // 🔒 evita colisão em concorrência ao gerar batch_no
            await conn.ExecuteAsync(new CommandDefinition(
                "select pg_advisory_xact_lock(hashtext(@k));",
                new { k = tenantId.ToString() },
                transaction: tx,
                cancellationToken: ct));

            // ✅ batch_no é INTEGER no banco
            const string sql = @"
with next_no as (
  select coalesce(max(b.batch_no), 0) + 1 as n
  from ged.batch b
  where b.tenant_id = @tenant_id
    and b.reg_status = 'A'
)
insert into ged.batch
  (id, tenant_id, batch_no, status, created_at, created_by, notes, reg_date, reg_status)
select
  gen_random_uuid(),
  @tenant_id,
  n,
  'RECEIVED'::ged.batch_status,
  now(),
  @created_by,
  @notes,
  now(),
  'A'
from next_no
returning id;
";

            var batchId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                created_by = userId,
                notes = vm.Notes
            }, transaction: tx, cancellationToken: ct));

            // histórico inicial (from=null, to=RECEIVED)
            await InsertBatchHistoryAsync(
                conn, tx,
                tenantId, batchId,
                fromStatus: null,
                toStatus: "RECEIVED",
                userId: userId,
                notes: "Lote criado",
                data: new { action = "CREATE" },
                eventType: "CREATE",
                ct: ct
            );

            // adiciona itens (se vierem)
            if (vm.DocumentIds is not null && vm.DocumentIds.Count > 0)
            {
                foreach (var docId in vm.DocumentIds.Distinct())
                {
                    if (docId == Guid.Empty) continue;

                    var exists = await DocumentExistsAsync(conn, tx, tenantId, docId, ct);
                    if (!exists)
                    {
                        tx.Rollback();
                        return Result<Guid>.Fail("DOC", $"Documento não encontrado: {docId}");
                    }

                    await InsertBatchItemAsync(conn, tx, tenantId, batchId, docId, vm.BoxId, ct);

                    // rastreio físico se boxId foi informado
                    if (vm.BoxId.HasValue && vm.BoxId.Value != Guid.Empty)
                    {
                        await InsertBoxHistoryAsync(conn, tx, tenantId, vm.BoxId.Value, docId, "ADD", userId,
                            $"Documento adicionado ao lote #{batchId} com vínculo à caixa.", ct);
                    }
                }
            }

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch", batchId,
                "Lote criado", null, null,
                new
                {
                    docs = vm.DocumentIds?.Count ?? 0,
                    boxId = vm.BoxId
                }, ct);

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

            var exists = await DocumentExistsAsync(conn, tx, tenantId, documentId, ct);
            if (!exists)
            {
                tx.Rollback();
                return Result.Fail("DOC", "Documento não encontrado.");
            }

            await InsertBatchItemAsync(conn, tx, tenantId, batchId, documentId, boxId, ct);

            if (boxId.HasValue && boxId.Value != Guid.Empty)
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, boxId.Value, documentId, "ADD", userId,
                    "Documento vinculado a caixa via lote.", ct);
            }

            var cur = await GetBatchStatusAsync(conn, tx, tenantId, batchId, ct);
            if (cur is not null)
            {
                await InsertBatchHistoryAsync(
                    conn, tx,
                    tenantId, batchId,
                    fromStatus: cur,
                    toStatus: cur,
                    userId: userId,
                    notes: "Item adicionado ao lote",
                    data: new { action = "ADD_ITEM", documentId, boxId },
                    eventType: "ADD_ITEM",
                    ct: ct
                );
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

            if (oldBoxId.HasValue && oldBoxId.Value != Guid.Empty &&
                (!newBoxId.HasValue || newBoxId.Value == Guid.Empty || newBoxId.Value != oldBoxId.Value))
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, oldBoxId.Value, documentId, "REMOVE", userId,
                    "Documento removido da caixa (movimentação).", ct);
            }

            if (newBoxId.HasValue && newBoxId.Value != Guid.Empty &&
                (!oldBoxId.HasValue || oldBoxId.Value == Guid.Empty || newBoxId.Value != oldBoxId.Value))
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, newBoxId.Value, documentId, "ADD", userId,
                    "Documento adicionado à caixa (movimentação).", ct);
            }

            var cur = await GetBatchStatusAsync(conn, tx, tenantId, batchId, ct);
            if (cur is not null)
            {
                await InsertBatchHistoryAsync(
                    conn, tx,
                    tenantId, batchId,
                    fromStatus: cur,
                    toStatus: cur,
                    userId: userId,
                    notes: "Movimentação de caixa do item",
                    data: new { action = "MOVE_ITEM_BOX", documentId, oldBoxId, newBoxId },
                    eventType: "MOVE_ITEM_BOX",
                    ct: ct
                );
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

            if (oldBoxId.HasValue && oldBoxId.Value != Guid.Empty)
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, oldBoxId.Value, documentId, "REMOVE", userId,
                    "Documento removido do lote (retirada da caixa vinculada).", ct);
            }

            var cur = await GetBatchStatusAsync(conn, tx, tenantId, batchId, ct);
            if (cur is not null)
            {
                await InsertBatchHistoryAsync(
                    conn, tx,
                    tenantId, batchId,
                    fromStatus: cur,
                    toStatus: cur,
                    userId: userId,
                    notes: "Item removido do lote",
                    data: new { action = "REMOVE_ITEM", documentId, oldBoxId },
                    eventType: "REMOVE_ITEM",
                    ct: ct
                );
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

            var toStatus = NormalizeStatus(newStatus);
            if (!AllowedStatus.Contains(toStatus))
                return Result.Fail("STATUS", "Status inválido. Use: RECEIVED, TRIAGE, DIGITIZATION, INDEXING, ARCHIVED.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            var fromStatus = await GetBatchStatusAsync(conn, tx, tenantId, batchId, ct);
            if (string.IsNullOrWhiteSpace(fromStatus))
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Lote não encontrado.");
            }

            const string sql = @"
update ged.batch
set status=@status::ged.batch_status,
    notes=coalesce(@notes, notes),
    updated_at=now(),
    updated_by=@updated_by
where tenant_id=@tenant_id and id=@batch_id and reg_status='A';
";

            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                status = toStatus,
                notes,
                updated_by = userId
            }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Lote não encontrado.");
            }

            await InsertBatchHistoryAsync(
                conn, tx,
                tenantId, batchId,
                fromStatus: fromStatus,
                toStatus: toStatus,
                userId: userId,
                notes: notes,
                data: new { action = "STATUS_CHANGE" },
                eventType: "STATUS_CHANGE",
                ct: ct
            );

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch", batchId,
                "Mudança de status do lote", null, null, new { fromStatus, toStatus, notes }, ct);

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
            "RECEBIDO" or "RECEIVED" => "RECEIVED",
            "TRIAGEM" or "TRIAGE" => "TRIAGE",
            "DIGITALIZACAO" or "DIGITALIZAÇÃO" or "DIGITIZACAO" or "DIGITIZATION" => "DIGITIZATION",
            "INDEXACAO" or "INDEXAÇÃO" or "INDEXING" => "INDEXING",
            "ARQUIVADO" or "ARQUIVAMENTO" or "ARCHIVED" => "ARCHIVED",
            _ => x
        };
    }

    private static async Task<bool> DocumentExistsAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid documentId,
        CancellationToken ct)
    {
        const string sql = @"
select 1
from ged.document d
where d.tenant_id=@tenant_id and d.id=@document_id
limit 1;";
        var ok = await conn.ExecuteScalarAsync<int?>(
            new CommandDefinition(sql, new { tenant_id = tenantId, document_id = documentId }, transaction: tx, cancellationToken: ct));
        return ok.HasValue;
    }

    private static async Task<string?> GetBatchStatusAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid batchId,
        CancellationToken ct)
    {
        const string sql = @"
select status::text
from ged.batch
where tenant_id=@tenant_id and id=@batch_id and reg_status='A';
";
        return await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, new { tenant_id = tenantId, batch_id = batchId }, transaction: tx, cancellationToken: ct));
    }

    private static async Task InsertBatchItemAsync(
        IDbConnection conn,
        IDbTransaction tx,
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
            box_id = (boxId.HasValue && boxId.Value != Guid.Empty) ? boxId : null
        }, transaction: tx, cancellationToken: ct));
    }

    private static async Task InsertBatchHistoryAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid batchId,
        string? fromStatus,
        string toStatus,
        Guid? userId,
        string? notes,
        object? data,
        string eventType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toStatus))
            throw new ArgumentException("toStatus é obrigatório.", nameof(toStatus));

        const string sql = @"
insert into ged.batch_history
(tenant_id, batch_id, from_status, to_status, changed_at, changed_by, notes, data, reg_date, reg_status, event_time, event_type)
values
(
  @tenant_id,
  @batch_id,
  @from_status::ged.batch_status,
  @to_status::ged.batch_status,
  now(),
  @changed_by,
  @notes,
  @data::jsonb,
  now(),
  'A',
  now(),
  @event_type
);
";

        var json = data is null ? "{}" : JsonSerializer.Serialize(data);

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            batch_id = batchId,
            from_status = string.IsNullOrWhiteSpace(fromStatus) ? null : NormalizeStatus(fromStatus),
            to_status = NormalizeStatus(toStatus),
            changed_by = userId,
            notes,
            data = json,
            event_type = eventType
        }, transaction: tx, cancellationToken: ct));
    }

    // ✅ FIX: insert do histórico físico agora é compatível com schemas diferentes
    private static async Task InsertBoxHistoryAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid boxId,
        Guid documentId,
        string eventType,
        Guid? userId,
        string? notes,
        CancellationToken ct)
    {
        // Descobre quais colunas existem (evita 42703)
        var cols = await GetColumnsAsync(conn, tx, "ged", "box_content_history", ct);

        string? timeCol =
            PickFirst(cols, "event_time", "event_at", "occurred_at", "created_at", "changed_at", "dt_evento");

        string? userCol =
            PickFirst(cols, "by_user_id", "user_id", "created_by", "changed_by", "actor_user_id");

        var hasNotes = cols.Contains("notes");
        var hasRegDate = cols.Contains("reg_date");
        var hasRegStatus = cols.Contains("reg_status");
        var hasEventType = cols.Contains("event_type");

        // Monta lista de colunas/valores dinamicamente
        var colList = new List<string> { "tenant_id", "box_id", "document_id" };
        var valList = new List<string> { "@tenant_id", "@box_id", "@document_id" };

        if (!string.IsNullOrWhiteSpace(timeCol))
        {
            colList.Add(timeCol);
            valList.Add("now()");
        }

        if (hasEventType)
        {
            colList.Add("event_type");
            valList.Add("@event_type");
        }

        if (!string.IsNullOrWhiteSpace(userCol))
        {
            colList.Add(userCol);
            valList.Add("@user_id");
        }

        if (hasNotes)
        {
            colList.Add("notes");
            valList.Add("@notes");
        }

        if (hasRegDate)
        {
            colList.Add("reg_date");
            valList.Add("now()");
        }

        if (hasRegStatus)
        {
            colList.Add("reg_status");
            valList.Add("'A'");
        }

        var sql = $@"
insert into ged.box_content_history
({string.Join(", ", colList)})
values
({string.Join(", ", valList)});
";

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            box_id = boxId,
            document_id = documentId,
            event_type = eventType,
            user_id = userId,
            notes
        }, transaction: tx, cancellationToken: ct));
    }

    private static string? PickFirst(HashSet<string> cols, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (cols.Contains(c)) return c;
        }
        return null;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        IDbConnection conn,
        IDbTransaction tx,
        string schema,
        string table,
        CancellationToken ct)
    {
        const string sql = @"
select lower(column_name) as column_name
from information_schema.columns
where table_schema = @schema
  and table_name   = @table;
";
        var list = await conn.QueryAsync<string>(new CommandDefinition(sql,
            new { schema, table }, transaction: tx, cancellationToken: ct));

        return new HashSet<string>(list ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }
}