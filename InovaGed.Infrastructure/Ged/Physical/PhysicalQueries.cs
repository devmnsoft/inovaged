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
            using var conn = await _db.OpenAsync(ct);

            const string sql = @"
select
  id,
  location_code as LocationCode,
  property_name as PropertyName,
  address_street as AddressStreet,
  address_number as AddressNumber,
  address_district as AddressDistrict,
  address_city as AddressCity,
  address_state as AddressState,
  address_zip as AddressZip,
  building as Building,
  room as Room,
  aisle as Aisle,
  rack as Rack,
  shelf as Shelf,
  pallet as Pallet
from ged.physical_location
where tenant_id=@tenant_id
  and reg_status='A'
  and (
    @q is null
    or coalesce(location_code,'') ilike ('%'||@q||'%')
    or coalesce(property_name,'') ilike ('%'||@q||'%')
    or coalesce(address_street,'') ilike ('%'||@q||'%')
    or coalesce(building,'') ilike ('%'||@q||'%')
  )
order by coalesce(property_name,''), coalesce(location_code,''), id;
";
            var list = await conn.QueryAsync<PhysicalLocationRowDto>(new CommandDefinition(sql, new { tenant_id = tenantId, q }, cancellationToken: ct));
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
            using var conn = await _db.OpenAsync(ct);

            const string sql = @"
select
  id as Id,
  location_code as LocationCode,
  property_name as PropertyName,
  address_street as AddressStreet,
  address_number as AddressNumber,
  address_district as AddressDistrict,
  address_city as AddressCity,
  address_state as AddressState,
  address_zip as AddressZip,
  building as Building,
  room as Room,
  aisle as Aisle,
  rack as Rack,
  shelf as Shelf,
  pallet as Pallet
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
            using var conn = await _db.OpenAsync(ct);

            const string sql = @"
select
  b.id,
  b.label_code as LabelCode,
  b.notes as Description,              -- <<<<<<<<<<<< trocado
  b.location_id as LocationId,
  pl.location_code as LocationCode
from ged.box b
left join ged.physical_location pl
  on pl.tenant_id=b.tenant_id
 and pl.id=b.location_id
where b.tenant_id=@tenant_id
  and b.reg_status='A'
  and (
    @q is null
    or b.label_code ilike ('%'||@q||'%')
    or coalesce(b.notes,'') ilike ('%'||@q||'%')          -- <<<<<<<<<<<< trocado
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
            using var conn = await _db.OpenAsync(ct);

            const string sql = @"
select
  id as Id,
  label_code as LabelCode,
  notes as Description,                -- <<<<<<<<<<<< trocado
  location_id as LocationId
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
     
}