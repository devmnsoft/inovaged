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
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm is null) return Result<Guid>.Fail("VM", "Dados inválidos.");

            await using var conn = await _db.OpenAsync(ct);

            if (vm.Id is null || vm.Id == Guid.Empty)
            {
                const string ins = @"
insert into ged.physical_location
(id, tenant_id, location_code, property_name,
 address_street, address_number, address_district, address_city, address_state, address_zip,
 building, room, aisle, rack, shelf, pallet, reg_date, reg_status)
values
(gen_random_uuid(), @tenant_id, @location_code, @property_name,
 @address_street, @address_number, @address_district, @address_city, @address_state, @address_zip,
 @building, @room, @aisle, @rack, @shelf, @pallet, now(), 'A')
returning id;
";
                var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(ins, new
                {
                    tenant_id = tenantId,
                    location_code = vm.LocationCode,
                    property_name = vm.PropertyName,
                    address_street = vm.AddressStreet,
                    address_number = vm.AddressNumber,
                    address_district = vm.AddressDistrict,
                    address_city = vm.AddressCity,
                    address_state = vm.AddressState,
                    address_zip = vm.AddressZip,
                    building = vm.Building,
                    room = vm.Room,
                    aisle = vm.Aisle,
                    rack = vm.Rack,
                    shelf = vm.Shelf,
                    pallet = vm.Pallet
                }, cancellationToken: ct));

                await _audit.WriteAsync(tenantId, userId, "CREATE", "physical_location", id,
                    "Localização criada", null, null, new { vm.LocationCode, vm.Building }, ct);

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
                    location_code = vm.LocationCode,
                    property_name = vm.PropertyName,
                    address_street = vm.AddressStreet,
                    address_number = vm.AddressNumber,
                    address_district = vm.AddressDistrict,
                    address_city = vm.AddressCity,
                    address_state = vm.AddressState,
                    address_zip = vm.AddressZip,
                    building = vm.Building,
                    room = vm.Room,
                    aisle = vm.Aisle,
                    rack = vm.Rack,
                    shelf = vm.Shelf,
                    pallet = vm.Pallet
                }, cancellationToken: ct));

                if (rows == 0) return Result<Guid>.Fail("NOTFOUND", "Localização não encontrada.");

                await _audit.WriteAsync(tenantId, userId, "UPDATE", "physical_location", vm.Id,
                    "Localização atualizada", null, null, new { vm.LocationCode, vm.Building }, ct);

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
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (id == Guid.Empty) return Result.Fail("ID", "Id inválido.");

            await using var conn = await _db.OpenAsync(ct);

            const string check = @"
select count(*)::int from ged.box
where tenant_id=@tenant_id and location_id=@id and reg_status='A';
";
            var cnt = await conn.ExecuteScalarAsync<int>(new CommandDefinition(check,
                new { tenant_id = tenantId, id }, cancellationToken: ct));
            if (cnt > 0) return Result.Fail("INUSE", "Não é possível excluir: há caixas vinculadas.");

            const string sql = @"
update ged.physical_location
set reg_status='I'
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
                new { tenant_id = tenantId, id }, cancellationToken: ct));
            if (rows == 0) return Result.Fail("NOTFOUND", "Localização não encontrada.");

            await _audit.WriteAsync(tenantId, userId, "DELETE", "physical_location", id,
                "Localização inativada", null, null, null, ct);

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
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm is null) return Result<Guid>.Fail("VM", "Dados inválidos.");
            if (string.IsNullOrWhiteSpace(vm.LabelCode)) return Result<Guid>.Fail("LABEL", "Etiqueta é obrigatória.");

            // BoxNo é obrigatório para insert — se não veio, gera pelo sequence
            await using var conn = await _db.OpenAsync(ct);

            if (vm.Id is null || vm.Id == Guid.Empty)
            {
                // FIX: inclui box_no na inserção; se não informado usa a sequence do banco
                string ins;
                object param;

                if (vm.BoxNo.HasValue && vm.BoxNo.Value > 0)
                {
                    ins = @"
insert into ged.box
(id, tenant_id, box_no, label_code, notes, location_id, reg_date, reg_status, created_at, updated_at)
values
(gen_random_uuid(), @tenant_id, @box_no, @label_code, @notes, @location_id, now(), 'A', now(), now())
returning id;
";
                    param = new
                    {
                        tenant_id = tenantId,
                        box_no = vm.BoxNo.Value,
                        label_code = vm.LabelCode.Trim(),
                        notes = vm.Notes,
                        location_id = vm.LocationId
                    };
                }
                else
                {
                    // Usa a sequence do banco para gerar o box_no automaticamente
                    ins = @"
insert into ged.box
(id, tenant_id, box_no, label_code, notes, location_id, reg_date, reg_status, created_at, updated_at)
values
(gen_random_uuid(), @tenant_id, nextval('ged.batch_no_seq')::int, @label_code, @notes, @location_id, now(), 'A', now(), now())
returning id;
";
                    param = new
                    {
                        tenant_id = tenantId,
                        label_code = vm.LabelCode.Trim(),
                        notes = vm.Notes,
                        location_id = vm.LocationId
                    };
                }

                var id = await conn.ExecuteScalarAsync<Guid>(
                    new CommandDefinition(ins, param, cancellationToken: ct));

                await _audit.WriteAsync(tenantId, userId, "CREATE", "box", id,
                    "Caixa criada", null, null, new { vm.BoxNo, vm.LabelCode, vm.LocationId }, ct);

                return Result<Guid>.Ok(id);
            }
            else
            {
                const string upd = @"
update ged.box
set label_code   = @label_code,
    notes        = @notes,
    location_id  = @location_id,
    box_no       = coalesce(@box_no, box_no),
    updated_at   = now()
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
                var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
                {
                    tenant_id = tenantId,
                    id = vm.Id,
                    box_no = vm.BoxNo,
                    label_code = vm.LabelCode.Trim(),
                    notes = vm.Notes,
                    location_id = vm.LocationId
                }, cancellationToken: ct));

                if (rows == 0) return Result<Guid>.Fail("NOTFOUND", "Caixa não encontrada.");

                await _audit.WriteAsync(tenantId, userId, "UPDATE", "box", vm.Id,
                    "Caixa atualizada", null, null, new { vm.BoxNo, vm.LabelCode, vm.LocationId }, ct);

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
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (id == Guid.Empty) return Result.Fail("ID", "Id inválido.");

            await using var conn = await _db.OpenAsync(ct);

            const string check = @"
select count(*)::int from ged.batch_item
where tenant_id=@tenant_id and box_id=@id and reg_status='A';
";
            var cnt = await conn.ExecuteScalarAsync<int>(new CommandDefinition(check,
                new { tenant_id = tenantId, id }, cancellationToken: ct));
            if (cnt > 0) return Result.Fail("INUSE", "Não é possível excluir: há documentos vinculados a esta caixa.");

            const string sql = @"
update ged.box set reg_status='I'
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
                new { tenant_id = tenantId, id }, cancellationToken: ct));
            if (rows == 0) return Result.Fail("NOTFOUND", "Caixa não encontrada.");

            await _audit.WriteAsync(tenantId, userId, "DELETE", "box", id,
                "Caixa inativada", null, null, null, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.DeleteBoxAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return Result.Fail("BOX", "Falha ao excluir caixa.");
        }
    }
}