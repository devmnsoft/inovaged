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

            // Item 17: itens do lote com label legível da caixa
            const string items = @"
select
  bi.document_id                                    as ""DocumentId"",
  bi.box_id                                         as ""BoxId"",
  coalesce(d.code,'')                               as ""DocumentCode"",
  coalesce(d.title,'')                              as ""DocumentTitle"",
  case
    when bx.id is not null
    then 'Caixa #' || bx.box_no::text || ' — ' || bx.label_code
    else null
  end                                               as ""BoxLabel""
from ged.batch_item bi
join ged.document d
  on d.tenant_id=bi.tenant_id
 and d.id=bi.document_id
left join ged.box bx
  on bx.tenant_id=bi.tenant_id
 and bx.id=bi.box_id
 and bx.reg_status='A'
where bi.tenant_id=@tenant_id
  and bi.batch_id=@batch_id
  and bi.reg_status='A'
order by d.title;
";
            var itemsList = (await conn.QueryAsync<BatchItemDto>(
                new CommandDefinition(items, new { tenant_id = tenantId, batch_id = batchId }, cancellationToken: ct))).AsList();

            // Item 18: histórico de fases com from_status via LAG
            const string hist = """
select
  coalesce(event_time, changed_at, reg_date) as "ChangedAt",
  coalesce(from_status::text, '')            as "FromStatus",
  coalesce(to_status::text, event_type, '')  as "ToStatus",
  coalesce(notes, '')                        as "Notes",
  coalesce(event_type, '')                   as "EventType"
from ged.batch_history
where tenant_id = @tenant_id
  and batch_id = @batch_id
  and reg_status = 'A'
order by coalesce(event_time, changed_at, reg_date) desc;
""";
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

    // Assinatura 1: sem folderId (compatibilidade com IBatchQueries)
    public async Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(
        Guid tenantId,
        string q,
        int limit,
        CancellationToken ct)
    {
        return await SearchDocumentsAsync(tenantId, q, limit, null, ct);
    }

    // Assinatura 2: com folderId opcional
    public async Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(
        Guid tenantId,
        string? q,
        int limit,
        string? folderId,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            q = (q ?? "").Trim();

            if (q.Length == 0 && folderId is null)
                return Array.Empty<DocumentPickDto>();

            Guid? folderGuid = Guid.TryParse(folderId, out var fg) ? fg : null;

            const string sql = @"
select
  d.id    as ""Id"",
  coalesce(d.code,'')  as ""Code"",
  coalesce(d.title,'') as ""Title""
from ged.document d
where d.tenant_id=@tenant_id
  and d.reg_status='A'
  and (@q = '' or (
        coalesce(d.code,'')  ilike ('%'||@q||'%')
     or coalesce(d.title,'') ilike ('%'||@q||'%')
  ))
  and (@folder_id is null or d.folder_id=@folder_id)
order by d.title
limit @limit;
";

            var rows = await conn.QueryAsync<DocumentPickDto>(
                new CommandDefinition(sql,
                    new { tenant_id = tenantId, q, limit, folder_id = folderGuid },
                    cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchQueries.SearchDocumentsAsync failed. Tenant={Tenant} Q={Q}", tenantId, q);
            return Array.Empty<DocumentPickDto>();
        }
    }
}

