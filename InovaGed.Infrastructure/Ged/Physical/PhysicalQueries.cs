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

            const string sql = """
select
  id                   as "Id",
  location_code        as "LocationCode",
  property_name        as "PropertyName",
  address_street       as "AddressStreet",
  address_number       as "AddressNumber",
  address_district     as "AddressDistrict",
  address_city         as "AddressCity",
  address_state        as "AddressState",
  address_zip          as "AddressZip",
  building             as "Building",
  room                 as "Room",
  aisle                as "Aisle",
  rack                 as "Rack",
  shelf                as "Shelf",
  pallet               as "Pallet",
  notes                as "Notes"
from ged.physical_location
where tenant_id=@tenant_id
  and reg_status='A'
  and (
    @q = ''
    or coalesce(location_code,'') ilike ('%'||@q||'%')
    or coalesce(property_name,'') ilike ('%'||@q||'%')
    or coalesce(address_street,'') ilike ('%'||@q||'%')
    or coalesce(building,'') ilike ('%'||@q||'%')
    or coalesce(room,'') ilike ('%'||@q||'%')
    or coalesce(aisle,'') ilike ('%'||@q||'%')
    or coalesce(rack,'') ilike ('%'||@q||'%')
    or coalesce(shelf,'') ilike ('%'||@q||'%')
    or coalesce(pallet,'') ilike ('%'||@q||'%')
  )
order by coalesce(property_name,''), coalesce(location_code,''), id;
""";

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

            const string sql = """
select
  id               as "Id",
  location_code    as "LocationCode",
  property_name    as "PropertyName",
  address_street   as "AddressStreet",
  address_number   as "AddressNumber",
  address_district as "AddressDistrict",
  address_city     as "AddressCity",
  address_state    as "AddressState",
  address_zip      as "AddressZip",
  building         as "Building",
  room             as "Room",
  aisle            as "Aisle",
  rack             as "Rack",
  shelf            as "Shelf",
  pallet           as "Pallet",
  notes            as "Notes"
from ged.physical_location
where tenant_id=@tenant_id
  and id=@id
  and reg_status='A';
""";

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

            const string sql = """
select
  b.id              as "Id",
  b.box_no          as "BoxNo",
  b.label_code      as "LabelCode",
  b.notes           as "Notes",
  b.location_id     as "LocationId",
  pl.location_code  as "LocationCode",
  pl.building       as "LocationBuilding",
  pl.room           as "LocationRoom"
from ged.box b
left join ged.physical_location pl
  on pl.tenant_id=b.tenant_id
 and pl.id=b.location_id
 and pl.reg_status='A'
where b.tenant_id=@tenant_id
  and b.reg_status='A'
  and (
    @q = ''
    or b.box_no::text ilike ('%'||@q||'%')
    or coalesce(b.label_code,'') ilike ('%'||@q||'%')
    or coalesce(b.notes,'') ilike ('%'||@q||'%')
    or coalesce(pl.location_code,'') ilike ('%'||@q||'%')
    or coalesce(pl.building,'') ilike ('%'||@q||'%')
    or coalesce(pl.room,'') ilike ('%'||@q||'%')
  )
order by b.box_no;
""";

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

            const string sql = """
select
  id           as "Id",
  box_no       as "BoxNo",
  label_code   as "LabelCode",
  notes        as "Notes",
  location_id  as "LocationId"
from ged.box
where tenant_id=@tenant_id
  and id=@id
  and reg_status='A';
""";

            return await conn.QuerySingleOrDefaultAsync<BoxFormVM>(
                new CommandDefinition(sql, new { tenant_id = tenantId, id }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.GetBoxAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return null;
        }
    }

    public async Task<IReadOnlyList<BoxContentItemDto>> GetBoxContentsAsync(Guid tenantId, Guid boxId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string sql = """
select
  bi.document_id          as "DocumentId",
  coalesce(d.code,'')     as "DocumentCode",
  coalesce(d.title,'')    as "DocumentTitle",
  b.batch_no::text        as "BatchNo",
  b.id                    as "BatchId",
  b.status::text          as "BatchStatus",
  bi.reg_date             as "AddedAt"
from ged.batch_item bi
join ged.document d
  on d.tenant_id=bi.tenant_id
 and d.id=bi.document_id
join ged.batch b
  on b.tenant_id=bi.tenant_id
 and b.id=bi.batch_id
 and b.reg_status='A'
where bi.tenant_id=@tenant_id
  and bi.box_id=@box_id
  and bi.reg_status='A'
order by d.title;
""";

            var rows = await conn.QueryAsync<BoxContentItemDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, box_id = boxId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.GetBoxContentsAsync failed. Tenant={Tenant} Box={Box}", tenantId, boxId);
            return Array.Empty<BoxContentItemDto>();
        }
    }

    public async Task<IReadOnlyList<AvailableDocumentForBoxDto>> ListDocumentsAvailableForBoxAsync(
        Guid tenantId,
        Guid boxId,
        string? q,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            q = (q ?? "").Trim();

            const string sql = """
select
  d.id                 as "DocumentId",
  coalesce(d.code,'')  as "DocumentCode",
  coalesce(d.title,'') as "DocumentTitle",
  b.id                 as "BatchId",
  b.batch_no::text     as "BatchNo",
  b.status::text       as "BatchStatus",
  bi.box_id            as "CurrentBoxId",
  case
    when bx.id is null then null
    else ('Caixa #' || bx.box_no::text || ' — ' || bx.label_code)
  end                  as "CurrentBoxLabel"
from ged.batch_item bi
join ged.document d
  on d.tenant_id=bi.tenant_id
 and d.id=bi.document_id
join ged.batch b
  on b.tenant_id=bi.tenant_id
 and b.id=bi.batch_id
 and b.reg_status='A'
left join ged.box bx
  on bx.tenant_id=bi.tenant_id
 and bx.id=bi.box_id
 and bx.reg_status='A'
where bi.tenant_id=@tenant_id
  and bi.reg_status='A'
  and (
    @q = ''
    or coalesce(d.code,'') ilike ('%'||@q||'%')
    or coalesce(d.title,'') ilike ('%'||@q||'%')
    or b.batch_no::text ilike ('%'||@q||'%')
  )
order by d.title, b.batch_no
limit 200;
""";

            var rows = await conn.QueryAsync<AvailableDocumentForBoxDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, box_id = boxId, q }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.ListDocumentsAvailableForBoxAsync failed. Tenant={Tenant} Box={Box}", tenantId, boxId);
            return Array.Empty<AvailableDocumentForBoxDto>();
        }
    }

    public async Task<IReadOnlyList<BoxHistoryRowDto>> GetBoxHistoryAsync(Guid tenantId, Guid boxId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string sql = """
select
  h.changed_at                        as "At",
  h.action                            as "EventType",
  coalesce(b.batch_no::text,'')        as "BatchNo",
  coalesce(b.id, '00000000-0000-0000-0000-000000000000'::uuid) as "BatchId",
  h.document_id                       as "DocumentId",
  coalesce(d.code,'')                 as "DocumentCode",
  coalesce(d.title,'')                as "DocumentTitle",
  h.box_id                            as "BoxId",
  h.old_box_id                        as "OldBoxId",
  h.new_box_id                        as "NewBoxId",
  case
    when oldb.id is null then null
    else ('Caixa #' || oldb.box_no::text || ' — ' || oldb.label_code)
  end                                 as "OldBoxLabel",
  case
    when newb.id is null then null
    else ('Caixa #' || newb.box_no::text || ' — ' || newb.label_code)
  end                                 as "NewBoxLabel",
  h.notes                             as "Notes"
from ged.box_content_history h
left join ged.batch b
  on b.tenant_id=h.tenant_id
 and b.id=h.batch_id
left join ged.document d
  on d.tenant_id=h.tenant_id
 and d.id=h.document_id
left join ged.box oldb
  on oldb.tenant_id=h.tenant_id
 and oldb.id=h.old_box_id
left join ged.box newb
  on newb.tenant_id=h.tenant_id
 and newb.id=h.new_box_id
where h.tenant_id=@tenant_id
  and h.reg_status='A'
  and (
      h.box_id=@box_id
      or h.old_box_id=@box_id
      or h.new_box_id=@box_id
  )
order by h.changed_at desc, h.id desc;
""";

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

    public async Task<IReadOnlyList<BoxLocationHistoryRowDto>> GetBoxLocationHistoryAsync(Guid tenantId, Guid boxId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string sql = @"
select
    h.changed_at as ""ChangedAt"",
    b.box_no as ""BoxNo"",
    b.label_code as ""LabelCode"",
    concat_ws(' / ', oldl.location_code, oldl.building, oldl.room, oldl.aisle, oldl.rack, oldl.shelf, oldl.pallet) as ""OldLocation"",
    concat_ws(' / ', newl.location_code, newl.building, newl.room, newl.aisle, newl.rack, newl.shelf, newl.pallet) as ""NewLocation"",
    h.notes as ""Notes""
from ged.box_location_history h
join ged.box b
  on b.tenant_id=h.tenant_id
 and b.id=h.box_id
left join ged.physical_location oldl
  on oldl.tenant_id=h.tenant_id
 and oldl.id=h.old_location_id
left join ged.physical_location newl
  on newl.tenant_id=h.tenant_id
 and newl.id=h.new_location_id
where h.tenant_id=@tenant_id
  and h.box_id=@box_id
  and h.reg_status='A'
order by h.changed_at desc, h.id desc;";

            var rows = await conn.QueryAsync<BoxLocationHistoryRowDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, box_id = boxId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.GetBoxLocationHistoryAsync failed. Tenant={Tenant} Box={Box}", tenantId, boxId);
            return Array.Empty<BoxLocationHistoryRowDto>();
        }
    }

    public async Task<IReadOnlyList<PhysicalMapRowDto>> GetPhysicalMapAsync(Guid tenantId, string? q, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            q = (q ?? "").Trim();

            const string sql = @"
select
    document_id     as ""DocumentId"",
    coalesce(document_code,'') as ""DocumentCode"",
    coalesce(document_title,'') as ""DocumentTitle"",
    batch_id        as ""BatchId"",
    coalesce(batch_no,'') as ""BatchNo"",
    coalesce(batch_status,'') as ""BatchStatus"",
    box_id          as ""BoxId"",
    box_no          as ""BoxNo"",
    label_code      as ""LabelCode"",
    location_id     as ""LocationId"",
    location_code   as ""LocationCode"",
    property_name   as ""PropertyName"",
    building        as ""Building"",
    room            as ""Room"",
    aisle           as ""Aisle"",
    rack            as ""Rack"",
    shelf           as ""Shelf"",
    pallet          as ""Pallet"",
    full_location   as ""FullLocation"",
    linked_at       as ""LinkedAt""
from ged.vw_physical_map
where tenant_id=@tenant_id
  and (
    @q = ''
    or coalesce(document_code,'') ilike ('%'||@q||'%')
    or coalesce(document_title,'') ilike ('%'||@q||'%')
    or coalesce(batch_no,'') ilike ('%'||@q||'%')
    or coalesce(label_code,'') ilike ('%'||@q||'%')
    or coalesce(location_code,'') ilike ('%'||@q||'%')
    or coalesce(full_location,'') ilike ('%'||@q||'%')
  )
order by coalesce(full_location,''), box_no, document_title
limit 1000;";

            var rows = await conn.QueryAsync<PhysicalMapRowDto>(
                new CommandDefinition(sql, new { tenant_id = tenantId, q }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalQueries.GetPhysicalMapAsync failed. Tenant={Tenant}", tenantId);
            return Array.Empty<PhysicalMapRowDto>();
        }
    }
}