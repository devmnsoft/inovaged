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
                const string ins = """
insert into ged.physical_location
(
    id,
    tenant_id,
    location_code,
    property_name,
    address_street,
    address_number,
    address_district,
    address_city,
    address_state,
    address_zip,
    building,
    room,
    aisle,
    rack,
    shelf,
    pallet,
    notes,
    reg_date,
    reg_status
)
values
(
    gen_random_uuid(),
    @tenant_id,
    @location_code,
    @property_name,
    @address_street,
    @address_number,
    @address_district,
    @address_city,
    @address_state,
    @address_zip,
    @building,
    @room,
    @aisle,
    @rack,
    @shelf,
    @pallet,
    @notes,
    now(),
    'A'
)
returning id;
""";

                var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(ins, new
                {
                    tenant_id = tenantId,
                    location_code = NullIfWhite(vm.LocationCode),
                    property_name = NullIfWhite(vm.PropertyName),
                    address_street = NullIfWhite(vm.AddressStreet),
                    address_number = NullIfWhite(vm.AddressNumber),
                    address_district = NullIfWhite(vm.AddressDistrict),
                    address_city = NullIfWhite(vm.AddressCity),
                    address_state = NullIfWhite(vm.AddressState),
                    address_zip = NullIfWhite(vm.AddressZip),
                    building = NullIfWhite(vm.Building),
                    room = NullIfWhite(vm.Room),
                    aisle = NullIfWhite(vm.Aisle),
                    rack = NullIfWhite(vm.Rack),
                    shelf = NullIfWhite(vm.Shelf),
                    pallet = NullIfWhite(vm.Pallet),
                    notes = NullIfWhite(vm.Notes)
                }, cancellationToken: ct));

                await _audit.WriteAsync(tenantId, userId, "CREATE", "physical_location", id,
                    "Localização física criada", null, null, new { vm.LocationCode, vm.Building, vm.Room }, ct);

                return Result<Guid>.Ok(id);
            }

            const string upd = """
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
    pallet=@pallet,
    notes=@notes
where tenant_id=@tenant_id
  and id=@id
  and reg_status='A';
""";

            var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
            {
                tenant_id = tenantId,
                id = vm.Id,
                location_code = NullIfWhite(vm.LocationCode),
                property_name = NullIfWhite(vm.PropertyName),
                address_street = NullIfWhite(vm.AddressStreet),
                address_number = NullIfWhite(vm.AddressNumber),
                address_district = NullIfWhite(vm.AddressDistrict),
                address_city = NullIfWhite(vm.AddressCity),
                address_state = NullIfWhite(vm.AddressState),
                address_zip = NullIfWhite(vm.AddressZip),
                building = NullIfWhite(vm.Building),
                room = NullIfWhite(vm.Room),
                aisle = NullIfWhite(vm.Aisle),
                rack = NullIfWhite(vm.Rack),
                shelf = NullIfWhite(vm.Shelf),
                pallet = NullIfWhite(vm.Pallet),
                notes = NullIfWhite(vm.Notes)
            }, cancellationToken: ct));

            if (rows == 0) return Result<Guid>.Fail("NOTFOUND", "Localização não encontrada.");

            await _audit.WriteAsync(tenantId, userId, "UPDATE", "physical_location", vm.Id,
                "Localização física atualizada", null, null, new { vm.LocationCode, vm.Building, vm.Room }, ct);

            return Result<Guid>.Ok(vm.Id.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.UpsertLocationAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("PHY", "Falha ao salvar localização física.");
        }
    }

    public async Task<Result> DeleteLocationAsync(Guid tenantId, Guid id, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (id == Guid.Empty) return Result.Fail("ID", "Id inválido.");

            await using var conn = await _db.OpenAsync(ct);

            const string check = """
select count(*)::int
from ged.box
where tenant_id=@tenant_id
  and location_id=@id
  and reg_status='A';
""";

            var cnt = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(check, new { tenant_id = tenantId, id }, cancellationToken: ct));

            if (cnt > 0)
                return Result.Fail("INUSE", "Não é possível excluir: há caixas vinculadas a esta localização.");

            const string sql = """
update ged.physical_location
set reg_status='I'
where tenant_id=@tenant_id
  and id=@id
  and reg_status='A';
""";

            var rows = await conn.ExecuteAsync(
                new CommandDefinition(sql, new { tenant_id = tenantId, id }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "Localização não encontrada.");

            await _audit.WriteAsync(tenantId, userId, "DELETE", "physical_location", id,
                "Localização física inativada", null, null, null, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.DeleteLocationAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return Result.Fail("PHY", "Falha ao excluir localização física.");
        }
    }

    public async Task<Result<Guid>> UpsertBoxAsync(Guid tenantId, Guid? userId, BoxFormVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm is null) return Result<Guid>.Fail("VM", "Dados inválidos.");
            if (string.IsNullOrWhiteSpace(vm.LabelCode)) return Result<Guid>.Fail("LABEL", "Etiqueta é obrigatória.");

            await using var conn = await _db.OpenAsync(ct);

            if (vm.Id is null || vm.Id == Guid.Empty)
            {
                const string ins = """
insert into ged.box
(
    id,
    tenant_id,
    box_no,
    label_code,
    notes,
    location_id,
    reg_date,
    reg_status,
    created_at,
    updated_at
)
values
(
    gen_random_uuid(),
    @tenant_id,
    coalesce(@box_no, nextval('ged.box_no_seq')::int),
    @label_code,
    @notes,
    @location_id,
    now(),
    'A',
    now(),
    now()
)
returning id;
""";

                var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(ins, new
                {
                    tenant_id = tenantId,
                    box_no = vm.BoxNo.HasValue && vm.BoxNo.Value > 0 ? vm.BoxNo.Value : (int?)null,
                    label_code = vm.LabelCode.Trim(),
                    notes = NullIfWhite(vm.Notes),
                    location_id = vm.LocationId
                }, cancellationToken: ct));

                await _audit.WriteAsync(tenantId, userId, "CREATE", "box", id,
                    "Caixa física criada", null, null, new { vm.BoxNo, vm.LabelCode, vm.LocationId }, ct);

                return Result<Guid>.Ok(id);
            }

            const string upd = """
update ged.box
set label_code=@label_code,
    notes=@notes,
    location_id=@location_id,
    box_no=coalesce(@box_no, box_no),
    updated_at=now()
where tenant_id=@tenant_id
  and id=@id
  and reg_status='A';
""";

            var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
            {
                tenant_id = tenantId,
                id = vm.Id,
                box_no = vm.BoxNo.HasValue && vm.BoxNo.Value > 0 ? vm.BoxNo.Value : (int?)null,
                label_code = vm.LabelCode.Trim(),
                notes = NullIfWhite(vm.Notes),
                location_id = vm.LocationId
            }, cancellationToken: ct));

            if (rows == 0) return Result<Guid>.Fail("NOTFOUND", "Caixa não encontrada.");

            await _audit.WriteAsync(tenantId, userId, "UPDATE", "box", vm.Id,
                "Caixa física atualizada", null, null, new { vm.BoxNo, vm.LabelCode, vm.LocationId }, ct);

            return Result<Guid>.Ok(vm.Id.Value);
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

            const string check = """
select count(*)::int
from ged.batch_item
where tenant_id=@tenant_id
  and box_id=@id
  and reg_status='A';
""";

            var cnt = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(check, new { tenant_id = tenantId, id }, cancellationToken: ct));

            if (cnt > 0)
                return Result.Fail("INUSE", "Não é possível excluir: há documentos vinculados a esta caixa.");

            const string sql = """
update ged.box
set reg_status='I',
    updated_at=now()
where tenant_id=@tenant_id
  and id=@id
  and reg_status='A';
""";

            var rows = await conn.ExecuteAsync(
                new CommandDefinition(sql, new { tenant_id = tenantId, id }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "Caixa não encontrada.");

            await _audit.WriteAsync(tenantId, userId, "DELETE", "box", id,
                "Caixa física inativada", null, null, null, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.DeleteBoxAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return Result.Fail("BOX", "Falha ao excluir caixa.");
        }
    }

    public async Task<Result> AddDocumentToBoxAsync(Guid tenantId, Guid? userId, BoxContentMaintenanceVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (vm.BoxId == Guid.Empty) return Result.Fail("BOX", "Caixa inválida.");
            if (vm.DocumentId == Guid.Empty) return Result.Fail("DOC", "Documento inválido.");

            await using var conn = await _db.OpenAsync(ct);

            const string findBatchSql = """
select bi.batch_id
from ged.batch_item bi
join ged.batch b
  on b.tenant_id=bi.tenant_id
 and b.id=bi.batch_id
 and b.reg_status='A'
where bi.tenant_id=@tenant_id
  and bi.document_id=@document_id
  and bi.reg_status='A'
order by bi.reg_date desc
limit 1;
""";

            var batchId = vm.BatchId;

            if (batchId is null || batchId == Guid.Empty)
            {
                batchId = await conn.ExecuteScalarAsync<Guid?>(
                    new CommandDefinition(findBatchSql, new
                    {
                        tenant_id = tenantId,
                        document_id = vm.DocumentId
                    }, cancellationToken: ct));
            }

            if (batchId is null || batchId == Guid.Empty)
                return Result.Fail("BATCH", "Documento não possui lote ativo para vínculo físico.");

            const string checkBoxSql = """
select count(*)::int
from ged.box
where tenant_id=@tenant_id
  and id=@box_id
  and reg_status='A';
""";

            var boxExists = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(checkBoxSql, new { tenant_id = tenantId, box_id = vm.BoxId }, cancellationToken: ct));

            if (boxExists == 0) return Result.Fail("BOX", "Caixa não encontrada.");

            const string upd = """
update ged.batch_item
set box_id=@box_id,
    reg_status='A'
where tenant_id=@tenant_id
  and batch_id=@batch_id
  and document_id=@document_id
  and reg_status='A';
""";

            var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = vm.DocumentId,
                box_id = vm.BoxId
            }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "Vínculo de lote/documento não encontrado.");

            await InsertBoxHistoryAsync(conn, tenantId, vm.BoxId, null, vm.BoxId, batchId.Value, vm.DocumentId, "ADD", userId, vm.Notes, ct);

            await _audit.WriteAsync(tenantId, userId, "UPDATE", "box_content", vm.BoxId,
                "Documento incluído na caixa", null, null, new { vm.BoxId, vm.DocumentId, BatchId = batchId, vm.Notes }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.AddDocumentToBoxAsync failed. Tenant={Tenant}", tenantId);
            return Result.Fail("BOXCONTENT", "Falha ao incluir documento na caixa.");
        }
    }

    public async Task<Result> RemoveDocumentFromBoxAsync(Guid tenantId, Guid? userId, BoxContentMaintenanceVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (vm.BoxId == Guid.Empty) return Result.Fail("BOX", "Caixa inválida.");
            if (vm.DocumentId == Guid.Empty) return Result.Fail("DOC", "Documento inválido.");

            await using var conn = await _db.OpenAsync(ct);

            const string selectSql = """
select batch_id
from ged.batch_item
where tenant_id=@tenant_id
  and box_id=@box_id
  and document_id=@document_id
  and reg_status='A'
order by reg_date desc
limit 1;
""";

            var batchId = await conn.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(selectSql, new
                {
                    tenant_id = tenantId,
                    box_id = vm.BoxId,
                    document_id = vm.DocumentId
                }, cancellationToken: ct));

            if (batchId is null || batchId == Guid.Empty)
                return Result.Fail("NOTFOUND", "Documento não está vinculado a esta caixa.");

            const string upd = """
update ged.batch_item
set box_id=null
where tenant_id=@tenant_id
  and box_id=@box_id
  and document_id=@document_id
  and batch_id=@batch_id
  and reg_status='A';
""";

            var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
            {
                tenant_id = tenantId,
                box_id = vm.BoxId,
                document_id = vm.DocumentId,
                batch_id = batchId
            }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "Documento não encontrado na caixa.");

            await InsertBoxHistoryAsync(conn, tenantId, vm.BoxId, vm.BoxId, null, batchId.Value, vm.DocumentId, "REMOVE", userId, vm.Notes, ct);

            await _audit.WriteAsync(tenantId, userId, "UPDATE", "box_content", vm.BoxId,
                "Documento removido da caixa", null, null, new { vm.BoxId, vm.DocumentId, BatchId = batchId, vm.Notes }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.RemoveDocumentFromBoxAsync failed. Tenant={Tenant}", tenantId);
            return Result.Fail("BOXCONTENT", "Falha ao remover documento da caixa.");
        }
    }

    public async Task<Result> MoveDocumentToBoxAsync(Guid tenantId, Guid? userId, BoxContentMaintenanceVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (vm.BoxId == Guid.Empty) return Result.Fail("BOX", "Caixa de destino inválida.");
            if (vm.DocumentId == Guid.Empty) return Result.Fail("DOC", "Documento inválido.");

            await using var conn = await _db.OpenAsync(ct);

            const string selectSql = """
select batch_id, box_id
from ged.batch_item
where tenant_id=@tenant_id
  and document_id=@document_id
  and reg_status='A'
order by reg_date desc
limit 1;
""";

            var current = await conn.QueryFirstOrDefaultAsync<dynamic>(
                new CommandDefinition(selectSql, new
                {
                    tenant_id = tenantId,
                    document_id = vm.DocumentId
                }, cancellationToken: ct));

            if (current is null)
                return Result.Fail("NOTFOUND", "Documento não possui vínculo ativo em lote.");

            Guid batchId = current.batch_id;
            Guid? oldBoxId = current.box_id;

            if (oldBoxId == vm.BoxId)
                return Result.Fail("SAMEBOX", "Documento já está nesta caixa.");

            const string upd = """
update ged.batch_item
set box_id=@new_box_id
where tenant_id=@tenant_id
  and batch_id=@batch_id
  and document_id=@document_id
  and reg_status='A';
""";

            var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
            {
                tenant_id = tenantId,
                batch_id = batchId,
                document_id = vm.DocumentId,
                new_box_id = vm.BoxId
            }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "Documento não encontrado para movimentação.");

            await InsertBoxHistoryAsync(conn, tenantId, vm.BoxId, oldBoxId, vm.BoxId, batchId, vm.DocumentId, "MOVE", userId, vm.Notes, ct);

            await _audit.WriteAsync(tenantId, userId, "UPDATE", "box_content", vm.BoxId,
                "Documento movimentado para outra caixa", null, null,
                new { OldBoxId = oldBoxId, NewBoxId = vm.BoxId, vm.DocumentId, BatchId = batchId, vm.Notes }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhysicalCommands.MoveDocumentToBoxAsync failed. Tenant={Tenant}", tenantId);
            return Result.Fail("BOXCONTENT", "Falha ao mover documento para caixa.");
        }
    }

    private static async Task InsertBoxHistoryAsync(
        System.Data.IDbConnection conn,
        Guid tenantId,
        Guid? boxId,
        Guid? oldBoxId,
        Guid? newBoxId,
        Guid batchId,
        Guid documentId,
        string action,
        Guid? userId,
        string? notes,
        CancellationToken ct)
    {
        const string sql = """
insert into ged.box_content_history
(
    tenant_id,
    box_id,
    old_box_id,
    new_box_id,
    batch_id,
    document_id,
    action,
    changed_at,
    changed_by,
    notes,
    data,
    reg_status
)
values
(
    @tenant_id,
    @box_id,
    @old_box_id,
    @new_box_id,
    @batch_id,
    @document_id,
    @action,
    now(),
    @changed_by,
    @notes,
    jsonb_build_object(
        'old_box_id', @old_box_id,
        'new_box_id', @new_box_id,
        'batch_id', @batch_id,
        'document_id', @document_id
    ),
    'A'
);
""";

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            tenant_id = tenantId,
            box_id = boxId,
            old_box_id = oldBoxId,
            new_box_id = newBoxId,
            batch_id = batchId,
            document_id = documentId,
            action,
            changed_by = userId,
            notes = NullIfWhite(notes)
        }, cancellationToken: ct));
    }

    private static string? NullIfWhite(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}