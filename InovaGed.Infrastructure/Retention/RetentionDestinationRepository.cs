using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Retention;

public sealed class RetentionDestinationRepository : IRetentionDestinationRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IPcdVersionResolver _pcd;
    private readonly IRetentionAuditWriter _audit;
    private readonly ILogger<RetentionDestinationRepository> _logger;

    public RetentionDestinationRepository(
        IDbConnectionFactory db,
        IPcdVersionResolver pcd,
        IRetentionAuditWriter audit,
        ILogger<RetentionDestinationRepository> logger)
    {
        _db = db;
        _pcd = pcd;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Guid> CreateBatchAsync(Guid tenantId, Guid userId, DestinationCreateRequest req, CancellationToken ct)
    {
        if (req.DocumentIds.Length == 0) throw new ArgumentException("Selecione ao menos 1 documento.");
        if (string.IsNullOrWhiteSpace(req.Destination)) throw new ArgumentException("Destino obrigatório.");

        var batchId = Guid.NewGuid();
        var pcdVersionId = await _pcd.GetLatestPublishedVersionIdAsync(tenantId, ct);

        const string sqlBatch = @"
insert into ged.retention_destination_batch(id, tenant_id, status, destination, pcd_version_id, notes, created_at, created_by)
values (@id, @tenantId, 'OPEN', @destination, @pcdVersionId, @notes, now(), @userId);";

        // Cria itens com snapshot (inclui HOLD ativo)
        const string sqlItems = @"
insert into ged.retention_destination_item(
  tenant_id, batch_id, document_id,
  classification_id, classification_code, classification_name,
  retention_basis_at, retention_due_at, retention_status,
  hold_active, hold_reason, created_at
)
select
  d.tenant_id,
  @batchId,
  d.id,
  d.classification_id,
  c.code,
  c.name,
  d.retention_basis_at,
  d.retention_due_at,
  d.retention_status,
  case when h.id is null then false else true end as hold_active,
  h.reason,
  now()
from ged.document d
left join ged.classification_plan c
  on c.tenant_id=d.tenant_id and c.id=d.classification_id
left join lateral (
  select hh.*
  from ged.retention_hold hh
  where hh.tenant_id=d.tenant_id and hh.document_id=d.id and hh.is_active=true
  order by hh.created_at desc
  limit 1
) h on true
where d.tenant_id=@tenantId
  and d.id = any(@ids);";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            await conn.ExecuteAsync(sqlBatch, new { id = batchId, tenantId, destination = req.Destination, pcdVersionId, notes = req.Notes, userId }, tx);

            await conn.ExecuteAsync(sqlItems, new { tenantId, batchId, ids = req.DocumentIds }, tx);

            await tx.CommitAsync(ct);

            // Auditoria
            foreach (var docId in req.DocumentIds)
                await _audit.WriteAsync(tenantId, userId, docId, "BATCH_CREATED", $"batch={batchId} dest={req.Destination}", ct);

            return batchId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateBatchAsync failed. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<IReadOnlyList<DestinationBatchRow>> ListBatchesAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
select
  id as Id,
  destination as Destination,
  status as Status,
  pcd_version_id as PcdVersionId,
  created_at as CreatedAt,
  created_by as CreatedBy,
  executed_at as ExecutedAt,
  executed_by as ExecutedBy
from ged.retention_destination_batch
where tenant_id=@tenantId
order by created_at desc
limit 200;";

        await using var conn = await _db.OpenAsync(ct);
        var list = await conn.QueryAsync<DestinationBatchRow>(sql, new { tenantId });
        return list.ToList();
    }

    public async Task<IReadOnlyList<DestinationItemRow>> GetBatchItemsAsync(Guid tenantId, Guid batchId, CancellationToken ct)
    {
        // ✅ Ajuste title/code se necessário
        const string sql = @"
select
  i.batch_id as BatchId,
  i.document_id as DocumentId,
  d.code as DocCode,
  d.title as DocTitle,
  i.classification_code as ClassificationCode,
  i.classification_name as ClassificationName,
  i.retention_basis_at as BasisAt,
  i.retention_due_at as DueAt,
  i.retention_status as RetentionStatus,
  i.hold_active as HoldActive,
  i.hold_reason as HoldReason
from ged.retention_destination_item i
join ged.document d
  on d.tenant_id=i.tenant_id and d.id=i.document_id
where i.tenant_id=@tenantId and i.batch_id=@batchId
order by i.retention_due_at nulls last, d.created_at desc;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<DestinationItemRow>(sql, new { tenantId, batchId });
        return rows.ToList();
    }

    public async Task<string> ExportBatchCsvAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct)
    {
        var items = await GetBatchItemsAsync(tenantId, batchId, ct);

        // Auditoria
        foreach (var it in items)
            await _audit.WriteAsync(tenantId, userId, it.DocumentId, "BATCH_EXPORTED", $"batch={batchId}", ct);

        static string Esc(string? s)
        {
            s ??= "";
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        var sb = new StringBuilder();
        sb.AppendLine("batch_id,document_id,doc_code,doc_title,class_code,class_name,basis_at,due_at,status,hold_active,hold_reason");

        foreach (var r in items)
        {
            sb.Append(Esc(r.BatchId.ToString())).Append(',')
              .Append(Esc(r.DocumentId.ToString())).Append(',')
              .Append(Esc(r.DocCode)).Append(',')
              .Append(Esc(r.DocTitle)).Append(',')
              .Append(Esc(r.ClassificationCode)).Append(',')
              .Append(Esc(r.ClassificationName)).Append(',')
              .Append(Esc(r.BasisAt?.ToString("yyyy-MM-dd HH:mm"))).Append(',')
              .Append(Esc(r.DueAt?.ToString("yyyy-MM-dd HH:mm"))).Append(',')
              .Append(Esc(r.RetentionStatus)).Append(',')
              .Append(Esc(r.HoldActive ? "true" : "false")).Append(',')
              .Append(Esc(r.HoldReason))
              .AppendLine();
        }

        // marca batch como EXPORTED
        const string sql = @"
update ged.retention_destination_batch
set status='EXPORTED'
where tenant_id=@tenantId and id=@batchId and status='OPEN';";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { tenantId, batchId });

        return sb.ToString();
    }

    public async Task ExecuteBatchAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct)
    {
        // Regra: NÃO executa itens com HOLD ativo.
        // Execução aqui = marcar no documento o destino (ex.: status=ELIMINADO)
        // ✅ Ajuste a coluna real do seu documento (ex.: doc_status, reg_status, is_deleted_logical etc.)

        const string sqlBatch = @"
select status, destination
from ged.retention_destination_batch
where tenant_id=@tenantId and id=@batchId
limit 1;";

        const string sqlItems = @"
select document_id
from ged.retention_destination_item
where tenant_id=@tenantId and batch_id=@batchId and hold_active=false;";

        const string sqlUpdateDoc = @"
update ged.document
set retention_status = 'EXECUTED',
    updated_at = now(),
    updated_by = @userId
where tenant_id=@tenantId and id = any(@ids);";

        const string sqlMarkBatch = @"
update ged.retention_destination_batch
set status='EXECUTED',
    executed_at=now(),
    executed_by=@userId
where tenant_id=@tenantId and id=@batchId;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var b = await conn.QueryFirstOrDefaultAsync<(string status, string destination)>(sqlBatch, new { tenantId, batchId }, tx);
            if (b.status is null) throw new InvalidOperationException("Batch não encontrado.");
            if (b.status == "CANCELED") throw new InvalidOperationException("Batch cancelado.");
            if (b.status == "EXECUTED") return;

            var ids = (await conn.QueryAsync<Guid>(sqlItems, new { tenantId, batchId }, tx)).ToArray();
            if (ids.Length == 0) throw new InvalidOperationException("Nenhum item executável (todos em HOLD?).");

            await conn.ExecuteAsync(sqlUpdateDoc, new { tenantId, userId, ids }, tx);
            await conn.ExecuteAsync(sqlMarkBatch, new { tenantId, batchId, userId }, tx);

            await tx.CommitAsync(ct);

            foreach (var docId in ids)
                await _audit.WriteAsync(tenantId, userId, docId, "BATCH_EXECUTED", $"batch={batchId}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteBatchAsync failed. Tenant={TenantId} Batch={BatchId}", tenantId, batchId);
            throw;
        }
    }
}