using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using InovaGed.Infrastructure.Common.Dapper;
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
  coalesce(nullif(c.title, ''), nullif(c.description, ''), nullif(c.code, ''), 'Sem classificação'),
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
        const string sql = """
select
  id as "Id",
  coalesce(destination, 'ELIMINAR') as "Destination",
  coalesce(status, 'OPEN') as "Status",
  pcd_version_id as "PcdVersionId",
  created_at as "CreatedAt",
  created_by as "CreatedBy",
  executed_at as "ExecutedAt",
  executed_by as "ExecutedBy"
from ged.retention_destination_batch
where tenant_id=@tenantId
order by created_at desc
limit 200
""";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<DestinationBatchDbRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        return rows.Select(x => new DestinationBatchRow(
            x.Id,
            string.IsNullOrWhiteSpace(x.Destination) ? "ELIMINAR" : x.Destination,
            string.IsNullOrWhiteSpace(x.Status) ? "OPEN" : x.Status,
            x.PcdVersionId, DapperValueConverters.ToDateTimeOffsetRequired(x.CreatedAt), x.CreatedBy,
            DapperValueConverters.ToDateTimeOffset(x.ExecutedAt), x.ExecutedBy)).ToList();
    }

    public async Task<IReadOnlyList<DestinationItemRow>> GetBatchItemsAsync(Guid tenantId, Guid batchId, CancellationToken ct)
    {
        // ✅ Ajuste title/code se necessário
        const string sql = """
select
  i.batch_id as "BatchId",
  i.document_id as "DocumentId",
  d.code as "DocCode",
  d.title as "DocTitle",
  i.classification_code as "ClassificationCode",
  i.classification_name as "ClassificationName",
  i.retention_basis_at as "BasisAt",
  i.retention_due_at as "DueAt",
  i.retention_status as "RetentionStatus",
  coalesce(i.hold_active, false) as "HoldActive",
  i.hold_reason as "HoldReason",
  i.block_reason as "BlockReason"
from ged.retention_destination_item i
join ged.document d
  on d.tenant_id=i.tenant_id and d.id=i.document_id
where i.tenant_id=@tenantId and i.batch_id=@batchId
order by i.retention_due_at nulls last, d.created_at desc
""";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<DestinationItemDbRow>(new CommandDefinition(sql, new { tenantId, batchId }, cancellationToken: ct));
        return rows.Select(x => new DestinationItemRow(
            x.BatchId, x.DocumentId, x.DocCode, x.DocTitle, x.ClassificationCode,
            x.ClassificationName, DapperValueConverters.ToDateTimeOffset(x.BasisAt), DapperValueConverters.ToDateTimeOffset(x.DueAt),
            x.RetentionStatus, x.HoldActive, x.HoldReason, x.BlockReason)).ToList();
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
        sb.AppendLine("batch_id,document_id,doc_code,doc_title,class_code,class_name,basis_at,due_at,status,hold_active,hold_reason,block_reason");

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
              .Append(Esc(r.HoldReason)).Append(',')
              .Append(Esc(r.BlockReason))
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

    public async Task<ExecuteBatchResult> ExecuteBatchAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct)
    {
        // RN Bloco C (C2): re-verificacao AO VIVO dos bloqueios por item antes de executar.
        // Bloqueios: hold legal (coluna do documento ou retencao_hold ativo), emprestimo fisico
        // em aberto, caixa em transito (movimentacao fisica via physical_box_document +
        // physical_loan por caixa) e protocolo pendente vinculado ao documento.
        // Itens bloqueados NAO executam; o motivo fica persistido no item (block_reason/blocked_at)
        // e auditado por documento. A execucao apenas marca o destino no documento.

        const string sqlBatch = @"
select status
from ged.retention_destination_batch
where tenant_id=@tenantId and id=@batchId
limit 1;";

        const string sqlItems = @"
select
  i.id as itemid,
  i.document_id as docid,
  coalesce(i.hold_active,false) as holdsnap,
  coalesce(d.retention_hold,false) as dochold,
  exists(select 1 from ged.retention_hold hh
         where hh.tenant_id=i.tenant_id and hh.document_id=i.document_id and hh.is_active=true) as holdtable,
  exists(select 1 from ged.physical_loan pl
         where pl.tenant_id=i.tenant_id and pl.document_id=i.document_id
           and pl.reg_status='A' and pl.status in ('OPEN','OVERDUE')) as loanopen,
  exists(select 1
         from ged.physical_box_document pbd
         join ged.physical_loan pl2 on pl2.tenant_id=pbd.tenant_id and pl2.box_id=pbd.box_id
         where pbd.tenant_id=i.tenant_id and pbd.document_id=i.document_id and pbd.reg_status='A'
           and pl2.reg_status='A' and pl2.status in ('OPEN','OVERDUE')) as boxtransit,
  exists(select 1 from ged.protocols pr
         where pr.tenant_id=i.tenant_id and pr.document_id=i.document_id) as protolink,
  exists(select 1
         from ged.protocol_request_item pri
         join ged.protocol_request prr on prr.id=pri.protocol_request_id and prr.tenant_id=i.tenant_id
         where pri.tenant_id=i.tenant_id and pri.document_id=i.document_id
           and pri.reg_status='A' and prr.reg_status='A'
           and upper(coalesce(prr.status,'')) not in ('FINISHED','COMPLETED','CLOSED','CANCELED','CANCELLED','ARQUIVADO')) as protopending
from ged.retention_destination_item i
join ged.document d on d.tenant_id=i.tenant_id and d.id=i.document_id
where i.tenant_id=@tenantId and i.batch_id=@batchId;";

        const string sqlClearBlock = @"
update ged.retention_destination_item
set block_reason=null, blocked_at=null
where tenant_id=@tenantId and id=@itemId;";

        const string sqlSetBlock = @"
update ged.retention_destination_item
set block_reason=@reason, blocked_at=now()
where tenant_id=@tenantId and id=@itemId;";

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
    executed_by=@userId,
    notes = coalesce(notes,'') || @suffix
where tenant_id=@tenantId and id=@batchId;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var bstatus = await conn.ExecuteScalarAsync<string>(sqlBatch, new { tenantId, batchId }, tx);
            if (bstatus is null) throw new InvalidOperationException("Batch não encontrado.");
            if (bstatus == "CANCELED") throw new InvalidOperationException("Batch cancelado.");
            if (bstatus == "EXECUTED") return new ExecuteBatchResult(true, 0, 0);

            var items = (await conn.QueryAsync<(
                    long ItemId, Guid DocId, bool HoldSnap, bool DocHold, bool HoldTable,
                    bool LoanOpen, bool BoxTransit, bool ProtoLink, bool ProtoPending)>(
                    sqlItems, new { tenantId, batchId }, tx)).ToList();

            var execIds = new List<Guid>();
            var blocked = new List<(long ItemId, Guid DocId, string Reason)>();

            foreach (var it in items)
            {
                var reasons = new List<string>();
                if (it.HoldSnap || it.DocHold || it.HoldTable) reasons.Add("Impedimento legal (hold ativo)");
                if (it.LoanOpen) reasons.Add("Empréstimo físico em aberto");
                if (it.BoxTransit) reasons.Add("Movimentação física em aberto (caixa em trânsito)");
                if (it.ProtoLink || it.ProtoPending) reasons.Add("Protocolo pendente vinculado ao documento");

                if (reasons.Count == 0) execIds.Add(it.DocId);
                else blocked.Add((it.ItemId, it.DocId, string.Join("; ", reasons)));
            }

            // Evidencia persistida: limpa bloqueio antigo nos agora executaveis, grava motivo nos bloqueados.
            foreach (var it in items.Where(x => execIds.Contains(x.DocId)))
                await conn.ExecuteAsync(sqlClearBlock, new { tenantId, itemId = it.ItemId }, tx);

            foreach (var bk in blocked)
                await conn.ExecuteAsync(sqlSetBlock, new { tenantId, itemId = bk.ItemId, reason = bk.Reason }, tx);

            if (execIds.Count > 0)
            {
                await conn.ExecuteAsync(sqlUpdateDoc, new { tenantId, userId, ids = execIds.Distinct().ToArray() }, tx);
                await conn.ExecuteAsync(sqlMarkBatch, new
                {
                    tenantId,
                    batchId,
                    userId,
                    suffix = blocked.Count > 0 ? $" | Execucao parcial: {execIds.Count} executado(s), {blocked.Count} bloqueado(s)." : ""
                }, tx);
            }

            await tx.CommitAsync(ct);

            // Auditoria por documento: WriteDocAsync grava em ged.document_audit (data jsonb com batch + motivo).
            // WriteAsync e so stub de log e nao persiste.
            foreach (var docId in execIds.Distinct())
                await _audit.WriteDocAsync(tenantId, userId, null, docId, "BATCH_EXECUTED", new { batch = batchId.ToString() }, ct);
            foreach (var bk in blocked)
                await _audit.WriteDocAsync(tenantId, userId, null, bk.DocId, "BATCH_BLOCKED", new { batch = batchId.ToString(), motivo = bk.Reason }, ct);

            return new ExecuteBatchResult(false, execIds.Count, blocked.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteBatchAsync failed. Tenant={TenantId} Batch={BatchId}", tenantId, batchId);
            throw;
        }
    }

    private sealed class DestinationBatchDbRow
    {
        public Guid Id { get; set; }
        public string? Destination { get; set; }
        public string? Status { get; set; }
        public Guid? PcdVersionId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public Guid? ExecutedBy { get; set; }
    }

    private sealed class DestinationItemDbRow
    {
        public Guid BatchId { get; set; }
        public Guid DocumentId { get; set; }
        public string? DocCode { get; set; }
        public string? DocTitle { get; set; }
        public string? ClassificationCode { get; set; }
        public string? ClassificationName { get; set; }
        public DateTime? BasisAt { get; set; }
        public DateTime? DueAt { get; set; }
        public string? RetentionStatus { get; set; }
        public bool HoldActive { get; set; }
        public string? HoldReason { get; set; }
        public string? BlockReason { get; set; }
    }
}
