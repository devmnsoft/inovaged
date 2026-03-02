using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Physical;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Physical;

public sealed class PhysicalCommands : IPhysicalCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<PhysicalCommands> _logger;

    public PhysicalCommands(IDbConnectionFactory db, IAuditWriter audit, ILogger<PhysicalCommands> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<Guid>> UpsertLocationAsync(Guid tenantId, Guid? userId, PhysicalLocationFormVM vm, CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            if (vm.Id is null || vm.Id == Guid.Empty)
            {
                const string ins = @"
insert into ged.physical_location
(id, tenant_id, location_code, property_name, address_street, address_number, address_district, address_city, address_state, address_zip,
 building, room, aisle, rack, shelf, pallet, reg_date, reg_status)
values
(gen_random_uuid(), @tenant_id, @location_code, @property_name, @address_street, @address_number, @address_district, @address_city, @address_state, @address_zip,
 @building, @room, @aisle, @rack, @shelf, @pallet, now(), 'A')
returning id;
";
                var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(ins, new
                {
                    tenant_id = tenantId,
                    vm.LocationCode,
                    vm.PropertyName,
                    vm.AddressStreet,
                    vm.AddressNumber,
                    vm.AddressDistrict,
                    vm.AddressCity,
                    vm.AddressState,
                    vm.AddressZip,
                    vm.Building,
                    vm.Room,
                    vm.Aisle,
                    vm.Rack,
                    vm.Shelf,
                    vm.Pallet
                }, cancellationToken: ct));

                await _audit.WriteAsync(tenantId, userId, "CREATE", "physical_location", id,
                    "Localização física criada", null, null, new { vm.LocationCode, vm.PropertyName }, ct);

                return Result<Guid>.Ok(id);
            }
            else
            {
                const string upd = @"
update ged.physical_location
set location_code=@location_code,
    property_name=@property_name,
    address_street=@address_street,
    address_number=@address_number,
    address_district=@address_district,
    address_city=@address_city,
    address_state=@address_state,
    address_zip=@address_zip,
    building=@building,
    room=@room,
    aisle=@aisle,
    rack=@rack,
    shelf=@shelf,
    pallet=@pallet
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
                var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
                {
                    tenant_id = tenantId,
                    id = vm.Id,
                    vm.LocationCode,
                    vm.PropertyName,
                    vm.AddressStreet,
                    vm.AddressNumber,
                    vm.AddressDistrict,
                    vm.AddressCity,
                    vm.AddressState,
                    vm.AddressZip,
                    vm.Building,
                    vm.Room,
                    vm.Aisle,
                    vm.Rack,
                    vm.Shelf,
                    vm.Pallet
                }, cancellationToken: ct));

                if (rows == 0) return Result<Guid>.Fail("NOTFOUND", "Localização não encontrada.");

                await _audit.WriteAsync(tenantId, userId, "UPDATE", "physical_location", vm.Id,
                    "Localização física atualizada", null, null, new { vm.LocationCode, vm.PropertyName }, ct);

                return Result<Guid>.Ok(vm.Id.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.UpsertLocationAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("PHY", "Falha ao salvar localização.");
        }
    }

    public async Task<Result> DeleteLocationAsync(Guid tenantId, Guid id, Guid? userId, CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            // bloqueia se tiver caixa vinculada
            const string check = @"select count(*) from ged.box where tenant_id=@tenant_id and location_id=@id and reg_status='A';";
            var cnt = await conn.ExecuteScalarAsync<int>(new CommandDefinition(check, new { tenant_id = tenantId, id }, cancellationToken: ct));
            if (cnt > 0) return Result.Fail("INUSE", "Não é possível excluir: há caixas vinculadas.");

            const string sql = @"update ged.physical_location set reg_status='I' where tenant_id=@tenant_id and id=@id and reg_status='A';";
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenant_id = tenantId, id }, cancellationToken: ct));
            if (rows == 0) return Result.Fail("NOTFOUND", "Localização não encontrada.");

            await _audit.WriteAsync(tenantId, userId, "DELETE", "physical_location", id,
                "Localização física inativada", null, null, null, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.DeleteLocationAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return Result.Fail("PHY", "Falha ao excluir localização.");
        }
    }

    public async Task<Result<Guid>> UpsertBoxAsync(Guid tenantId, Guid? userId, BoxFormVM vm, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(vm.LabelCode)) return Result<Guid>.Fail("LABEL", "LabelCode é obrigatório.");

            using var conn = await _db.OpenAsync(ct);

            if (vm.Id is null || vm.Id == Guid.Empty)
            {
                const string ins = @"
insert into ged.box
(id, tenant_id, label_code, description, location_id, reg_date, reg_status)
values
(gen_random_uuid(), @tenant_id, @label_code, @description, @location_id, now(), 'A')
returning id;
";
                var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(ins, new
                {
                    tenant_id = tenantId,
                    label_code = vm.LabelCode,
                    description = vm.Description,
                    location_id = vm.LocationId
                }, cancellationToken: ct));

                await _audit.WriteAsync(tenantId, userId, "CREATE", "box", id,
                    "Caixa criada", null, null, new { vm.LabelCode, vm.LocationId }, ct);

                return Result<Guid>.Ok(id);
            }
            else
            {
                const string upd = @"
update ged.box
set label_code=@label_code,
    description=@description,
    location_id=@location_id
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
                var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
                {
                    tenant_id = tenantId,
                    id = vm.Id,
                    label_code = vm.LabelCode,
                    description = vm.Description,
                    location_id = vm.LocationId
                }, cancellationToken: ct));

                if (rows == 0) return Result<Guid>.Fail("NOTFOUND", "Caixa não encontrada.");

                await _audit.WriteAsync(tenantId, userId, "UPDATE", "box", vm.Id,
                    "Caixa atualizada", null, null, new { vm.LabelCode, vm.LocationId }, ct);

                return Result<Guid>.Ok(vm.Id.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.UpsertBoxAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("BOX", "Falha ao salvar caixa.");
        }
    }

    public async Task<Result> DeleteBoxAsync(Guid tenantId, Guid id, Guid? userId, CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);

            const string sql = @"update ged.box set reg_status='I' where tenant_id=@tenant_id and id=@id and reg_status='A';";
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenant_id = tenantId, id }, cancellationToken: ct));
            if (rows == 0) return Result.Fail("NOTFOUND", "Caixa não encontrada.");

            await _audit.WriteAsync(tenantId, userId, "DELETE", "box", id, "Caixa inativada", null, null, null, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.DeleteBoxAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return Result.Fail("BOX", "Falha ao excluir caixa.");
        }
    }
}