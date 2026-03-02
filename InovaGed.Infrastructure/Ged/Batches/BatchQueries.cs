using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Batches;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Batches;

public sealed class BatchQueries : IBatchQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<BatchQueries> _logger;

    public BatchQueries(IDbConnectionFactory db, ILogger<BatchQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BatchRowDto>> ListAsync(
        Guid tenantId,
        string? q,
        string? status,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            q = (q ?? "").Trim();

            const string sql = @"
select
  b.id                as ""Id"",
  b.batch_no          as ""BatchNo"",
  b.status::text      as ""Status"",
  coalesce(b.notes,'') as ""Notes"",
  b.created_at        as ""CreatedAt"",
  b.items_count       as ""ItemsCount""
from ged.batch b
where b.tenant_id=@tenant_id
  and b.reg_status='A'
  and (
       @q = ''
       or b.batch_no::text ilike ('%'||@q||'%')
       or coalesce(b.notes,'') ilike ('%'||@q||'%')
  )
  and (@status is null or b.status::text=@status)
order by b.created_at desc;
";

            var list = await conn.QueryAsync<BatchRowDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, q, status }, cancellationToken: ct));

            return list.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchQueries.ListAsync failed. Tenant={Tenant}", tenantId);
            return Array.Empty<BatchRowDto>();
        }
    }

    public async Task<(BatchRowDto Header, List<BatchItemDto> Items, List<BatchHistoryDto> History)?> GetAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string head = @"
select
  b.id                 as ""Id"",
  b.batch_no           as ""BatchNo"",
  b.status::text       as ""Status"",
  coalesce(b.notes,'') as ""Notes"",
  b.created_at         as ""CreatedAt"",
  (
    select count(*)::int
    from ged.batch_item bi
    where bi.tenant_id=b.tenant_id
      and bi.batch_id=b.id
      and bi.reg_status='A'
  ) as ""ItemsCount""
from ged.batch b
where b.tenant_id=@tenant_id
  and b.id=@batch_id
  and b.reg_status='A';
";

            var header = await conn.QuerySingleOrDefaultAsync<BatchRowDto>(
                new CommandDefinition(head, new { tenant_id = tenantId, batch_id = batchId }, cancellationToken: ct));

            if (header is null) return null;

            const string items = @"
select
  bi.document_id as DocumentId,
  bi.box_id as BoxId,
  d.code as DocumentCode,
  d.title as DocumentTitle
from ged.batch_item bi
join ged.document d
  on d.tenant_id=bi.tenant_id
 and d.id=bi.document_id
where bi.tenant_id=@tenant_id
  and bi.batch_id=@batch_id
  and bi.reg_status='A'
order by d.title;
";
            var itemsList = (await conn.QueryAsync<BatchItemDto>(
                new CommandDefinition(items, new { tenant_id = tenantId, batch_id = batchId }, cancellationToken: ct))).AsList();

            const string hist = @"
select
  changed_at as ChangedAt,
  from_status::text as FromStatus,
  to_status::text as ToStatus,
  notes as Notes
from ged.batch_history
where tenant_id=@tenant_id
  and batch_id=@batch_id
  and reg_status='A'
order by changed_at desc;
";
            var histList = (await conn.QueryAsync<BatchHistoryDto>(
                new CommandDefinition(hist, new { tenant_id = tenantId, batch_id = batchId }, cancellationToken: ct))).AsList();

            return (header, itemsList, histList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchQueries.GetAsync failed. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return null;
        }
    }
}