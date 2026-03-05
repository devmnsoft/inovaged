using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Batches;
using InovaGed.Application.Ged.Loans;
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

            // ✅ sem b.items_count (não existe) -> calcula via subquery
            const string sql = @"
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

            // ✅ itens do lote + box (se quiser exibir evidência de guarda física)
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

            // ✅ histórico por evento/fase (consistente com BatchCommands e com PoC item 18)
            const string hist = @"
select
  coalesce(changed_at, reg_date, event_time) as ""ChangedAt"",
  from_status as ""FromStatus"",
  to_status as ""ToStatus"",
  notes as ""Notes""
from ged.batch_history
where tenant_id=@tenant_id
  and batch_id=@batch_id
  and reg_status='A'
order by coalesce(changed_at, reg_date, event_time) desc;
";
            var histList = (await conn.QueryAsync<BatchHistoryDto>(
                new CommandDefinition(hist, new { tenant_id = tenantId, batch_id = batchId }, cancellationToken: ct))).AsList();

            // 🔧 compatibilidade: se seu BatchHistoryDto tiver FromStatus,
            // ele vai ficar null (ok). Se quiser preencher FromStatus com base
            // em "lag()", eu implemento depois.

            return (header, itemsList, histList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchQueries.GetAsync failed. Tenant={Tenant} Batch={Batch}", tenantId, batchId);
            return null;
        }
    }


    public async Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(
      Guid tenantId, string? q, int take, string? status, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            q = (q ?? "").Trim();
            take = Math.Clamp(take, 5, 50);
            status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();

            const string sql = @"
select
  d.id as ""Id"",
  d.code as ""Code"",
  d.title as ""Title"",
  d.status::text as ""Status"",
  d.created_at as ""CreatedAt""
from ged.document d
where d.tenant_id=@tenant_id
  and (
     @q = ''
     or d.code ilike ('%'||@q||'%')
     or d.title ilike ('%'||@q||'%')
     or coalesce(d.description,'') ilike ('%'||@q||'%')
  )
  and (@status is null or d.status::text = @status)
order by d.created_at desc
limit @take;
";

            var rows = await conn.QueryAsync<DocumentPickDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, q, take, status }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchQueries.SearchDocumentsAsync failed. Tenant={Tenant}", tenantId);
            return Array.Empty<DocumentPickDto>();
        }
    }

    public async Task<IReadOnlyList<DocumentSearchDto>> SearchDocumentsAsync(
    Guid tenantId,
    string q,
    int take,
    CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            q = (q ?? "").Trim();
            if (q.Length < 3) return Array.Empty<DocumentSearchDto>();

            take = (take <= 0 || take > 50) ? 20 : take;

            const string sql = @"
select
  d.id          as ""Id"",
  d.code        as ""Code"",
  d.title       as ""Title"",
  d.status::text as ""Status"",
  d.created_at  as ""CreatedAt""
from ged.document d
where d.tenant_id = @tenant_id
  and (
       d.code  ilike ('%' || @q || '%')
    or d.title ilike ('%' || @q || '%')
  )
order by d.created_at desc
limit @take;
";

            var list = await conn.QueryAsync<DocumentSearchDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, q, take }, cancellationToken: ct));

            return list.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchQueries.SearchDocumentsAsync failed. Tenant={Tenant}", tenantId);
            return Array.Empty<DocumentSearchDto>();
        }
    }
}