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
            if (tenantId == Guid.Empty)
                return Result<Guid>.Fail("TENANT", "Tenant inválido.");

            if (vm is null)
                return Result<Guid>.Fail("VM", "Dados do lote inválidos.");

            vm.DocumentIds = vm.DocumentIds?
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList() ?? new List<Guid>();

            if (vm.BoxId.HasValue && vm.BoxId.Value == Guid.Empty)
                vm.BoxId = null;

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            if (vm.BoxId.HasValue)
            {
                var boxExists = await BoxExistsAsync(conn, tx, tenantId, vm.BoxId.Value, ct);
                if (!boxExists)
                {
                    tx.Rollback();
                    return Result<Guid>.Fail("BOX", "A caixa informada não foi encontrada ou está inativa.");
                }
            }

            await conn.ExecuteAsync(new CommandDefinition(
                "select pg_advisory_xact_lock(hashtext(@key));",
                new { key = $"batch:{tenantId}" },
                transaction: tx,
                cancellationToken: ct));

            const string sql = """
            with next_no as (
                select coalesce(max(b.batch_no), 0) + 1 as n
                from ged.batch b
                where b.tenant_id = @tenant_id
                  and b.reg_status = 'A'
            )
            insert into ged.batch
            (
                id,
                tenant_id,
                batch_no,
                status,
                created_at,
                created_by,
                notes,
                reg_date,
                reg_status
            )
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
            """;

            var batchId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                created_by = userId,
                notes = vm.Notes
            }, transaction: tx, cancellationToken: ct));

            await InsertBatchHistoryAsync(
                conn,
                tx,
                tenantId,
                batchId,
                null,
                "RECEIVED",
                userId,
                "Lote criado em etapa inicial de recebimento.",
                new
                {
                    action = "CREATE",
                    documents = vm.DocumentIds.Count,
                    boxId = vm.BoxId
                },
                "CREATE",
                ct);

            foreach (var docId in vm.DocumentIds)
            {
                var exists = await DocumentExistsAsync(conn, tx, tenantId, docId, ct);

                if (!exists)
                {
                    tx.Rollback();
                    return Result<Guid>.Fail("DOC", $"Documento não encontrado: {docId}");
                }

                await InsertBatchItemAsync(conn, tx, tenantId, batchId, docId, vm.BoxId, ct);

                await InsertBatchHistoryAsync(
                    conn,
                    tx,
                    tenantId,
                    batchId,
                    "RECEIVED",
                    "RECEIVED",
                    userId,
                    "Documento incluído no lote durante a criação.",
                    new
                    {
                        action = "ADD_ITEM_ON_CREATE",
                        documentId = docId,
                        boxId = vm.BoxId
                    },
                    "ADD_ITEM",
                    ct);

                if (vm.BoxId.HasValue)
                {
                    await InsertBoxHistoryAsync(
                        conn,
                        tx,
                        tenantId,
                        vm.BoxId.Value,
                        docId,
                        "ADD",
                        userId,
                        "Documento vinculado à caixa física durante criação do lote.",
                        ct);
                }
            }

            await WriteOperationalLogAsync(
                conn,
                tx,
                tenantId,
                userId,
                "BATCHES",
                "batch",
                batchId,
                "CREATE",
                "Lote criado com sucesso.",
                "INFO",
                new
                {
                    documents = vm.DocumentIds.Count,
                    boxId = vm.BoxId,
                    notes = vm.Notes
                },
                ct);

            tx.Commit();

            await _audit.WriteAsync(
                tenantId,
                userId,
                "BATCH_EVENT",
                "batch",
                batchId,
                "Lote criado com sucesso",
                null,
                null,
                new
                {
                    documents = vm.DocumentIds.Count,
                    boxId = vm.BoxId
                },
                ct);

            return Result<Guid>.Ok(batchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar lote. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("BATCH", "Falha ao criar lote. Verifique os dados informados e tente novamente.");
        }
    }

    public async Task<Result> AddItemAsync(Guid tenantId, Guid batchId, Guid documentId, Guid? boxId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (batchId == Guid.Empty) return Result.Fail("BATCH", "Lote inválido.");
            if (documentId == Guid.Empty) return Result.Fail("DOC", "Documento inválido.");

            if (boxId.HasValue && boxId.Value == Guid.Empty)
                boxId = null;

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            if (!await BatchExistsAsync(conn, tx, tenantId, batchId, ct))
            {
                tx.Rollback();
                return Result.Fail("BATCH", "Lote não encontrado.");
            }

            if (!await DocumentExistsAsync(conn, tx, tenantId, documentId, ct))
            {
                tx.Rollback();
                return Result.Fail("DOC", "Documento não encontrado.");
            }

            if (boxId.HasValue && !await BoxExistsAsync(conn, tx, tenantId, boxId.Value, ct))
            {
                tx.Rollback();
                return Result.Fail("BOX", "A caixa informada não foi encontrada ou está inativa.");
            }

            var inserted = await InsertBatchItemAsync(conn, tx, tenantId, batchId, documentId, boxId, ct);

            if (!inserted)
            {
                tx.Rollback();
                return Result.Fail("DUPLICATE", "Este documento já está vinculado ao lote.");
            }

            if (boxId.HasValue)
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, boxId.Value, documentId, "ADD", userId,
                    "Documento vinculado à caixa física via lote.", ct);
            }

            var currentStatus = await GetBatchStatusAsync(conn, tx, tenantId, batchId, ct) ?? "RECEIVED";

            await InsertBatchHistoryAsync(
                conn,
                tx,
                tenantId,
                batchId,
                currentStatus,
                currentStatus,
                userId,
                "Documento adicionado ao lote.",
                new { action = "ADD_ITEM", documentId, boxId },
                "ADD_ITEM",
                ct);

            await WriteOperationalLogAsync(conn, tx, tenantId, userId, "BATCHES", "batch_item", batchId,
                "ADD_ITEM", "Documento adicionado ao lote.", "INFO", new { documentId, boxId }, ct);

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch_item", batchId,
                "Documento adicionado ao lote", null, null, new { documentId, boxId }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao adicionar documento ao lote. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao adicionar documento ao lote.");
        }
    }

    public async Task<Result> MoveItemBoxAsync(Guid tenantId, Guid batchId, Guid documentId, Guid? newBoxId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (batchId == Guid.Empty) return Result.Fail("BATCH", "Lote inválido.");
            if (documentId == Guid.Empty) return Result.Fail("DOC", "Documento inválido.");

            if (newBoxId.HasValue && newBoxId.Value == Guid.Empty)
                newBoxId = null;

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            if (newBoxId.HasValue && !await BoxExistsAsync(conn, tx, tenantId, newBoxId.Value, ct))
            {
                tx.Rollback();
                return Result.Fail("BOX", "A nova caixa informada não foi encontrada ou está inativa.");
            }

            const string getOld = """
            select box_id
            from ged.batch_item
            where tenant_id = @tenant_id
              and batch_id = @batch_id
              and document_id = @document_id
              and reg_status = 'A';
            """;

            var oldBoxId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(getOld, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = documentId
            }, transaction: tx, cancellationToken: ct));

            const string sql = """
            update ged.batch_item
            set box_id = @box_id
            where tenant_id = @tenant_id
              and batch_id = @batch_id
              and document_id = @document_id
              and reg_status = 'A';
            """;

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
                return Result.Fail("NOTFOUND", "Documento não encontrado neste lote.");
            }

            if (oldBoxId.HasValue && oldBoxId.Value != Guid.Empty && oldBoxId != newBoxId)
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, oldBoxId.Value, documentId, "REMOVE", userId,
                    "Documento removido da caixa anterior.", ct);
            }

            if (newBoxId.HasValue && newBoxId.Value != Guid.Empty && newBoxId != oldBoxId)
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, newBoxId.Value, documentId, "ADD", userId,
                    "Documento vinculado à nova caixa.", ct);
            }

            var currentStatus = await GetBatchStatusAsync(conn, tx, tenantId, batchId, ct) ?? "RECEIVED";

            await InsertBatchHistoryAsync(
                conn,
                tx,
                tenantId,
                batchId,
                currentStatus,
                currentStatus,
                userId,
                "Movimentação física do documento entre caixas.",
                new { action = "MOVE_ITEM_BOX", documentId, oldBoxId, newBoxId },
                "MOVE_ITEM_BOX",
                ct);

            await WriteOperationalLogAsync(conn, tx, tenantId, userId, "BATCHES", "batch_item", batchId,
                "MOVE_ITEM_BOX", "Documento movimentado entre caixas.", "INFO",
                new { documentId, oldBoxId, newBoxId }, ct);

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch_item", batchId,
                "Documento movimentado entre caixas", null, null, new { documentId, oldBoxId, newBoxId }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao mover documento do lote. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao movimentar documento.");
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

            const string getOld = """
            select box_id
            from ged.batch_item
            where tenant_id = @tenant_id
              and batch_id = @batch_id
              and document_id = @document_id
              and reg_status = 'A';
            """;

            var oldBoxId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(getOld, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = documentId
            }, transaction: tx, cancellationToken: ct));

            const string sql = """
            update ged.batch_item
            set reg_status = 'I'
            where tenant_id = @tenant_id
              and batch_id = @batch_id
              and document_id = @document_id
              and reg_status = 'A';
            """;

            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = documentId
            }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Documento não encontrado neste lote.");
            }

            if (oldBoxId.HasValue && oldBoxId.Value != Guid.Empty)
            {
                await InsertBoxHistoryAsync(conn, tx, tenantId, oldBoxId.Value, documentId, "REMOVE", userId,
                    "Documento removido do lote e desvinculado da caixa.", ct);
            }

            var currentStatus = await GetBatchStatusAsync(conn, tx, tenantId, batchId, ct) ?? "RECEIVED";

            await InsertBatchHistoryAsync(
                conn,
                tx,
                tenantId,
                batchId,
                currentStatus,
                currentStatus,
                userId,
                "Documento removido do lote.",
                new { action = "REMOVE_ITEM", documentId, oldBoxId },
                "REMOVE_ITEM",
                ct);

            await WriteOperationalLogAsync(conn, tx, tenantId, userId, "BATCHES", "batch_item", batchId,
                "REMOVE_ITEM", "Documento removido do lote.", "INFO",
                new { documentId, oldBoxId }, ct);

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch_item", batchId,
                "Documento removido do lote", null, null, new { documentId, oldBoxId }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao remover documento do lote. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao remover documento do lote.");
        }
    }

    public async Task<Result> ChangeStatusAsync(Guid tenantId, Guid batchId, string newStatus, Guid? userId, string? notes, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (batchId == Guid.Empty) return Result.Fail("BATCH", "Lote inválido.");
            if (string.IsNullOrWhiteSpace(newStatus)) return Result.Fail("STATUS", "Etapa inválida.");

            var toStatus = NormalizeStatus(newStatus);

            if (!AllowedStatus.Contains(toStatus))
                return Result.Fail("STATUS", "Etapa inválida.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            var fromStatus = await GetBatchStatusAsync(conn, tx, tenantId, batchId, ct);

            if (string.IsNullOrWhiteSpace(fromStatus))
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Lote não encontrado.");
            }

            const string sql = """
            update ged.batch
            set status = @status::ged.batch_status,
                notes = coalesce(@notes, notes),
                updated_at = now(),
                updated_by = @updated_by
            where tenant_id = @tenant_id
              and id = @batch_id
              and reg_status = 'A';
            """;

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
                conn,
                tx,
                tenantId,
                batchId,
                fromStatus,
                toStatus,
                userId,
                notes ?? $"Etapa alterada de {fromStatus} para {toStatus}.",
                new { action = "STATUS_CHANGE", fromStatus, toStatus },
                "STATUS_CHANGE",
                ct);

            await WriteOperationalLogAsync(conn, tx, tenantId, userId, "BATCHES", "batch", batchId,
                "STATUS_CHANGE", "Etapa do lote alterada.", "INFO",
                new { fromStatus, toStatus, notes }, ct);

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch", batchId,
                "Mudança de etapa do lote", null, null, new { fromStatus, toStatus, notes }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao alterar etapa do lote. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao alterar etapa do lote.");
        }
    }

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

    private static async Task<bool> BatchExistsAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid batchId, CancellationToken ct)
    {
        const string sql = """
        select 1
        from ged.batch
        where tenant_id = @tenant_id
          and id = @batch_id
          and reg_status = 'A'
        limit 1;
        """;

        var value = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            batch_id = batchId
        }, transaction: tx, cancellationToken: ct));

        return value.HasValue;
    }

    private static async Task<bool> DocumentExistsAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = """
        select 1
        from ged.document
        where tenant_id = @tenant_id
          and id = @document_id
          and reg_status = 'A'
        limit 1;
        """;

        var value = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            document_id = documentId
        }, transaction: tx, cancellationToken: ct));

        return value.HasValue;
    }

    private static async Task<bool> BoxExistsAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid boxId, CancellationToken ct)
    {
        const string sql = """
        select 1
        from ged.box
        where tenant_id = @tenant_id
          and id = @box_id
          and reg_status = 'A'
        limit 1;
        """;

        var value = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            box_id = boxId
        }, transaction: tx, cancellationToken: ct));

        return value.HasValue;
    }

    private static async Task<string?> GetBatchStatusAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid batchId, CancellationToken ct)
    {
        const string sql = """
        select status::text
        from ged.batch
        where tenant_id = @tenant_id
          and id = @batch_id
          and reg_status = 'A';
        """;

        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            batch_id = batchId
        }, transaction: tx, cancellationToken: ct));
    }

    private static async Task<bool> InsertBatchItemAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid batchId, Guid documentId, Guid? boxId, CancellationToken ct)
    {
        const string sql = """
        insert into ged.batch_item
        (
            tenant_id,
            batch_id,
            document_id,
            box_id,
            reg_date,
            reg_status
        )
        values
        (
            @tenant_id,
            @batch_id,
            @document_id,
            @box_id,
            now(),
            'A'
        )
        on conflict do nothing;
        """;

        var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            batch_id = batchId,
            document_id = documentId,
            box_id = boxId
        }, transaction: tx, cancellationToken: ct));

        return rows > 0;
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
        const string sql = """
        insert into ged.batch_history
        (
            tenant_id,
            batch_id,
            from_status,
            to_status,
            changed_at,
            changed_by,
            notes,
            data,
            reg_date,
            reg_status,
            event_time,
            event_type
        )
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
        """;

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
        var columns = await GetColumnsAsync(conn, tx, "ged", "box_content_history", ct);

        string? timeCol = PickFirst(columns, "event_time", "event_at", "occurred_at", "created_at", "changed_at", "dt_evento");
        string? userCol = PickFirst(columns, "by_user_id", "user_id", "created_by", "changed_by", "actor_user_id");

        var colList = new List<string> { "tenant_id", "box_id", "document_id" };
        var valList = new List<string> { "@tenant_id", "@box_id", "@document_id" };

        if (!string.IsNullOrWhiteSpace(timeCol))
        {
            colList.Add(timeCol);
            valList.Add("now()");
        }

        if (columns.Contains("event_type"))
        {
            colList.Add("event_type");
            valList.Add("@event_type");
        }

        if (!string.IsNullOrWhiteSpace(userCol))
        {
            colList.Add(userCol);
            valList.Add("@user_id");
        }

        if (columns.Contains("notes"))
        {
            colList.Add("notes");
            valList.Add("@notes");
        }

        if (columns.Contains("reg_date"))
        {
            colList.Add("reg_date");
            valList.Add("now()");
        }

        if (columns.Contains("reg_status"))
        {
            colList.Add("reg_status");
            valList.Add("'A'");
        }

        var sql = $"""
        insert into ged.box_content_history
        ({string.Join(", ", colList)})
        values
        ({string.Join(", ", valList)});
        """;

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

    private static async Task WriteOperationalLogAsync(
        IDbConnection conn,
        IDbTransaction tx,
        Guid tenantId,
        Guid? userId,
        string module,
        string entityName,
        Guid? entityId,
        string action,
        string message,
        string severity,
        object? data,
        CancellationToken ct)
    {
        const string sql = """
        insert into ged.operational_event_log
        (
            tenant_id,
            user_id,
            module,
            entity_name,
            entity_id,
            action,
            message,
            severity,
            data,
            created_at,
            reg_status
        )
        values
        (
            @tenant_id,
            @user_id,
            @module,
            @entity_name,
            @entity_id,
            @action,
            @message,
            @severity,
            @data::jsonb,
            now(),
            'A'
        );
        """;

        var json = data is null ? "{}" : JsonSerializer.Serialize(data);

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            user_id = userId,
            module,
            entity_name = entityName,
            entity_id = entityId,
            action,
            message,
            severity,
            data = json
        }, transaction: tx, cancellationToken: ct));
    }

    private static string? PickFirst(HashSet<string> cols, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (cols.Contains(candidate))
                return candidate;
        }

        return null;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(IDbConnection conn, IDbTransaction tx, string schema, string table, CancellationToken ct)
    {
        const string sql = """
        select lower(column_name) as column_name
        from information_schema.columns
        where table_schema = @schema
          and table_name = @table;
        """;

        var list = await conn.QueryAsync<string>(new CommandDefinition(sql, new
        {
            schema,
            table
        }, transaction: tx, cancellationToken: ct));

        return new HashSet<string>(list ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Result> UpdateAsync(
      Guid tenantId,
      Guid batchId,
      string? notes,
      Guid? userId,
      CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty)
                return Result.Fail("TENANT", "Tenant inválido.");

            if (batchId == Guid.Empty)
                return Result.Fail("BATCH", "Lote inválido.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string getSql = """
        select status::text
        from ged.batch
        where tenant_id = @tenant_id
          and id = @batch_id
          and reg_status = 'A';
        """;

            var currentStatus = await conn.ExecuteScalarAsync<string?>(
                new CommandDefinition(
                    getSql,
                    new
                    {
                        tenant_id = tenantId,
                        batch_id = batchId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            if (string.IsNullOrWhiteSpace(currentStatus))
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Lote não encontrado.");
            }

            /*
             * ATENÇÃO:
             * Sua tabela ged.batch não possui updated_at nem updated_by.
             * Por isso o update abaixo altera apenas notes.
             */
            const string updateSql = """
        update ged.batch
        set notes = @notes
        where tenant_id = @tenant_id
          and id = @batch_id
          and reg_status = 'A';
        """;

            var rows = await conn.ExecuteAsync(
                new CommandDefinition(
                    updateSql,
                    new
                    {
                        tenant_id = tenantId,
                        batch_id = batchId,
                        notes
                    },
                    transaction: tx,
                    cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Lote não encontrado para atualização.");
            }

            const string historySql = """
        insert into ged.batch_history
        (
            tenant_id,
            batch_id,
            from_status,
            to_status,
            changed_at,
            changed_by,
            notes,
            data,
            reg_date,
            reg_status,
            event_time,
            event_type
        )
        values
        (
            @tenant_id,
            @batch_id,
            @current_status::ged.batch_status,
            @current_status::ged.batch_status,
            now(),
            @changed_by,
            @history_notes,
            @data::jsonb,
            now(),
            'A',
            now(),
            'UPDATE'
        );
        """;

            await conn.ExecuteAsync(
                new CommandDefinition(
                    historySql,
                    new
                    {
                        tenant_id = tenantId,
                        batch_id = batchId,
                        current_status = currentStatus,
                        changed_by = userId,
                        history_notes = "Dados básicos do lote atualizados.",
                        data = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            action = "UPDATE",
                            notes
                        })
                    },
                    transaction: tx,
                    cancellationToken: ct));

            tx.Commit();

            await _audit.WriteAsync(
                tenantId,
                userId,
                "BATCH_EVENT",
                "batch",
                batchId,
                "Dados básicos do lote atualizados",
                null,
                null,
                new
                {
                    notes
                },
                ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar lote. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao atualizar dados do lote.");
        }
    }

    public async Task<Result> DeleteAsync(
    Guid tenantId,
    Guid batchId,
    Guid? userId,
    string? reason,
    CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty)
                return Result.Fail("TENANT", "Tenant inválido.");

            if (batchId == Guid.Empty)
                return Result.Fail("BATCH", "Lote inválido.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string getSql = """
        select status::text
        from ged.batch
        where tenant_id = @tenant_id
          and id = @batch_id
          and reg_status = 'A';
        """;

            var currentStatus = await conn.ExecuteScalarAsync<string?>(
                new CommandDefinition(
                    getSql,
                    new
                    {
                        tenant_id = tenantId,
                        batch_id = batchId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            if (string.IsNullOrWhiteSpace(currentStatus))
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Lote não encontrado ou já excluído.");
            }

            if (currentStatus == "ARCHIVED")
            {
                tx.Rollback();
                return Result.Fail("ARCHIVED", "Lotes arquivados não podem ser excluídos. Reabra ou registre uma ocorrência administrativa.");
            }

            const string deleteItemsSql = """
        update ged.batch_item
        set reg_status = 'I'
        where tenant_id = @tenant_id
          and batch_id = @batch_id
          and reg_status = 'A';
        """;

            await conn.ExecuteAsync(
                new CommandDefinition(
                    deleteItemsSql,
                    new
                    {
                        tenant_id = tenantId,
                        batch_id = batchId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            const string deleteBatchSql = """
        update ged.batch
        set reg_status = 'I'
        where tenant_id = @tenant_id
          and id = @batch_id
          and reg_status = 'A';
        """;

            var rows = await conn.ExecuteAsync(
                new CommandDefinition(
                    deleteBatchSql,
                    new
                    {
                        tenant_id = tenantId,
                        batch_id = batchId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Não foi possível excluir o lote.");
            }

            const string historySql = """
        insert into ged.batch_history
        (
            tenant_id,
            batch_id,
            from_status,
            to_status,
            changed_at,
            changed_by,
            notes,
            data,
            reg_date,
            reg_status,
            event_time,
            event_type
        )
        values
        (
            @tenant_id,
            @batch_id,
            @current_status::ged.batch_status,
            @current_status::ged.batch_status,
            now(),
            @changed_by,
            @notes,
            @data::jsonb,
            now(),
            'A',
            now(),
            'DELETE'
        );
        """;

            await conn.ExecuteAsync(
                new CommandDefinition(
                    historySql,
                    new
                    {
                        tenant_id = tenantId,
                        batch_id = batchId,
                        current_status = currentStatus,
                        changed_by = userId,
                        notes = string.IsNullOrWhiteSpace(reason)
                            ? "Lote excluído logicamente."
                            : $"Lote excluído logicamente. Motivo: {reason}",
                        data = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            action = "DELETE",
                            reason,
                            previousStatus = currentStatus
                        })
                    },
                    transaction: tx,
                    cancellationToken: ct));

            tx.Commit();

            await _audit.WriteAsync(
                tenantId,
                userId,
                "BATCH_EVENT",
                "batch",
                batchId,
                "Lote excluído logicamente",
                null,
                null,
                new
                {
                    reason,
                    previousStatus = currentStatus
                },
                ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir lote. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao excluir lote.");
        }
    }
}