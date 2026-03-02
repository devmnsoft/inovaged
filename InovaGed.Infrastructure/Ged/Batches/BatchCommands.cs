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
            if (string.IsNullOrWhiteSpace(vm.BatchNo)) return Result<Guid>.Fail("BATCHNO", "BatchNo é obrigatório.");

            using var conn = await _db.OpenAsync(ct);
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
                batch_no = vm.BatchNo,
                created_by = userId,
                notes = vm.Notes
            }, transaction: tx, cancellationToken: ct));

            if (vm.DocumentIds.Count > 0)
            {
                const string ins = @"
insert into ged.batch_item(tenant_id, batch_id, document_id, box_id, reg_date, reg_status)
values(@tenant_id, @batch_id, @document_id, @box_id, now(), 'A')
on conflict do nothing;
";
                foreach (var docId in vm.DocumentIds.Distinct())
                {
                    await conn.ExecuteAsync(new CommandDefinition(ins, new
                    {
                        tenant_id = tenantId,
                        batch_id = batchId,
                        document_id = docId,
                        box_id = vm.BoxId
                    }, transaction: tx, cancellationToken: ct));
                }
            }

            tx.Commit();

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch", batchId,
                "Lote criado", null, null, new { vm.BatchNo, docs = vm.DocumentIds.Count }, ct);

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
            using var conn = await _db.OpenAsync(ct);

            const string sql = @"
insert into ged.batch_item(tenant_id, batch_id, document_id, box_id, reg_date, reg_status)
values(@tenant_id, @batch_id, @document_id, @box_id, now(), 'A')
on conflict do nothing;
";
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = documentId,
                box_id = boxId
            }, cancellationToken: ct));

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
            using var conn = await _db.OpenAsync(ct);

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
            }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "Item não encontrado no lote.");

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch_item", batchId,
                "Movimentação de caixa do item", null, null, new { documentId, newBoxId }, ct);

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
            using var conn = await _db.OpenAsync(ct);

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
            }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "Item não encontrado no lote.");

            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch_item", batchId,
                "Item removido do lote", null, null, new { documentId }, ct);

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
            if (string.IsNullOrWhiteSpace(newStatus)) return Result.Fail("STATUS", "Status inválido.");

            using var conn = await _db.OpenAsync(ct);

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
                status = newStatus,
                notes
            }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "Lote não encontrado.");

            // trigger do banco já grava batch_history + audit_log (se você aplicou o SQL).
            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "batch", batchId,
                "Mudança de status do lote", null, null, new { status = newStatus, notes }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchCommands.ChangeStatusAsync failed. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return Result.Fail("BATCH", "Falha ao alterar status do lote.");
        }
    }
}