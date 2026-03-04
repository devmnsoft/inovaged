using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Physical;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Physical;

public sealed class PhysicalQueries : IPhysicalQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<PhysicalQueries> _logger;

    public PhysicalQueries(IDbConnectionFactory db, ILogger<PhysicalQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PhysicalLocationRowDto>> ListLocationsAsync(Guid tenantId, string? q, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            q = (q ?? "").Trim();

            const string sql = @"
select
  id,
  location_code as ""LocationCode"",
  property_name as ""PropertyName"",
  address_street as ""AddressStreet"",
  address_number as ""AddressNumber"",
  address_district as ""AddressDistrict"",
  address_city as ""AddressCity"",
  address_state as ""AddressState"",
  address_zip as ""AddressZip"",
  building as ""Building"",
  room as ""Room"",
  aisle as ""Aisle"",
  rack as ""Rack"",
  shelf as ""Shelf"",
  pallet as ""Pallet""
from ged.physical_location
where tenant_id=@tenant_id
  and reg_status='A'
  and (
    @q = ''
    or coalesce(location_code,'') ilike ('%'||@q||'%')
    or coalesce(property_name,'') ilike ('%'||@q||'%')
    or coalesce(address_street,'') ilike ('%'||@q||'%')
    or coalesce(building,'') ilike ('%'||@q||'%')
  )
order by coalesce(property_name,''), coalesce(location_code,''), id;
";
            var list = await conn.QueryAsync<PhysicalLocationRowDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, q }, cancellationToken: ct));

            return list.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.ListLocationsAsync failed. Tenant={Tenant}", tenantId);
            return Array.Empty<PhysicalLocationRowDto>();
        }
    }

    public async Task<PhysicalLocationFormVM?> GetLocationAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string sql = @"
select
  id as ""Id"",
  location_code as ""LocationCode"",
  property_name as ""PropertyName"",
  address_street as ""AddressStreet"",
  address_number as ""AddressNumber"",
  address_district as ""AddressDistrict"",
  address_city as ""AddressCity"",
  address_state as ""AddressState"",
  address_zip as ""AddressZip"",
  building as ""Building"",
  room as ""Room"",
  aisle as ""Aisle"",
  rack as ""Rack"",
  shelf as ""Shelf"",
  pallet as ""Pallet""
from ged.physical_location
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
            return await conn.QuerySingleOrDefaultAsync<PhysicalLocationFormVM>(
                new CommandDefinition(sql, new { tenant_id = tenantId, id }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.GetLocationAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return null;
        }
    }

    public async Task<IReadOnlyList<BoxRowDto>> ListBoxesAsync(Guid tenantId, string? q, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            q = (q ?? "").Trim();

            const string sql = @"
select
  b.id as ""Id"",
  b.label_code as ""LabelCode"",
  b.notes as ""Notes"",
  b.location_id as ""LocationId"",
  pl.location_code as ""LocationCode""
from ged.box b
left join ged.physical_location pl
  on pl.tenant_id=b.tenant_id
 and pl.id=b.location_id
where b.tenant_id=@tenant_id
  and b.reg_status='A'
  and (
    @q = ''
    or coalesce(b.label_code,'') ilike ('%'||@q||'%')
    or coalesce(b.notes,'') ilike ('%'||@q||'%')
    or coalesce(pl.location_code,'') ilike ('%'||@q||'%')
  )
order by b.label_code;
";
            var list = await conn.QueryAsync<BoxRowDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, q }, cancellationToken: ct));

            return list.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.ListBoxesAsync failed. Tenant={Tenant}", tenantId);
            return Array.Empty<BoxRowDto>();
        }
    }

    public async Task<BoxFormVM?> GetBoxAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string sql = @"
select
  id as ""Id"",
  label_code as ""LabelCode"",
  notes as ""Notes"",
  location_id as ""LocationId""
from ged.box
where tenant_id=@tenant_id
  and id=@id
  and reg_status='A';
";
            return await conn.QuerySingleOrDefaultAsync<BoxFormVM>(
                new CommandDefinition(sql, new { tenant_id = tenantId, id }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.GetBoxAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return null;
        }
    }

    public async Task<IReadOnlyList<BoxHistoryRowDto>> GetBoxHistoryAsync(Guid tenantId, Guid boxId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            // ✅ Histórico baseado no que já existe na PoC:
            // - vínculo doc↔caixa: ged.batch_item.box_id
            // - fases do lote: ged.batch_history (event_time/event_type/notes)
            const string sql = @"
select
  coalesce(h.event_time, b.created_at) as ""At"",
  coalesce(h.event_type, b.status::text) as ""EventType"",
  b.batch_no as ""BatchNo"",
  b.id as ""BatchId"",
  bi.document_id as ""DocumentId"",
  coalesce(d.code,'') as ""DocumentCode"",
  coalesce(d.title,'') as ""DocumentTitle"",
  h.notes as ""Notes""
from ged.batch_item bi
join ged.batch b
  on b.tenant_id=bi.tenant_id and b.id=bi.batch_id and b.reg_status='A'
join ged.document d
  on d.tenant_id=bi.tenant_id and d.id=bi.document_id
left join ged.batch_history h
  on h.tenant_id=b.tenant_id and h.batch_id=b.id and h.reg_status='A'
where bi.tenant_id=@tenant_id
  and bi.box_id=@box_id
  and bi.reg_status='A'
order by coalesce(h.event_time, b.created_at) desc, b.batch_no, d.title;
";

            var rows = await conn.QueryAsync<BoxHistoryRowDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, box_id = boxId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.GetBoxHistoryAsync failed. Tenant={Tenant} Box={Box}", tenantId, boxId);
            return Array.Empty<BoxHistoryRowDto>();
        }
    }
}