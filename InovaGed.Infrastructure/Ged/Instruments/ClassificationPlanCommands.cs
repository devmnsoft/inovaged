using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Instruments;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Instruments;

public sealed class ClassificationPlanCommands : IClassificationPlanCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<ClassificationPlanCommands> _logger;

    public ClassificationPlanCommands(IDbConnectionFactory db, IAuditWriter audit, ILogger<ClassificationPlanCommands> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, ClassificationPlanCreateVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm is null) return Result<Guid>.Fail("VM", "Dados inválidos.");
            if (string.IsNullOrWhiteSpace(vm.Code)) return Result<Guid>.Fail("CODE", "Código é obrigatório.");
            if (string.IsNullOrWhiteSpace(vm.Name)) return Result<Guid>.Fail("NAME", "Nome é obrigatório.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string sql = @"
insert into ged.classification_plan
(id, tenant_id, code, name, description, parent_id,
 retention_start_event, retention_active_days, retention_active_months, retention_active_years,
 retention_archive_days, retention_archive_months, retention_archive_years,
 final_destination, requires_digital_signature, is_confidential, is_active,
 created_at, created_by, retention_notes)
values
(gen_random_uuid(), @tenant_id, @code, @name, @description, @parent_id,
 @retention_start_event::ged.retention_start_event, @rad, @ram, @ray,
 @rrd, @rrm, @rry,
 @final_destination::ged.final_destination, @reqsig, @conf, @active,
 now(), @by, @rnotes)
returning id;
";
            var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                code = vm.Code.Trim(),
                name = vm.Name.Trim(),
                description = vm.Description,
                parent_id = vm.ParentId,
                retention_start_event = vm.RetentionStartEvent,
                rad = vm.RetentionActiveDays,
                ram = vm.RetentionActiveMonths,
                ray = vm.RetentionActiveYears,
                rrd = vm.RetentionArchiveDays,
                rrm = vm.RetentionArchiveMonths,
                rry = vm.RetentionArchiveYears,
                final_destination = vm.FinalDestination,
                reqsig = vm.RequiresDigitalSignature,
                conf = vm.IsConfidential,
                active = vm.IsActive,
                by = userId,
                rnotes = vm.RetentionNotes
            }, transaction: tx, cancellationToken: ct));

            // history snapshot (Item 4 trilha)
            const string hist = @"
insert into ged.classification_plan_history
(tenant_id, classification_id, changed_at, changed_by, change_reason,
 code, name, parent_id, retention_start_event,
 retention_active_days, retention_active_months, retention_active_years,
 retention_archive_days, retention_archive_months, retention_archive_years,
 final_destination, requires_digital_signature, is_confidential, is_active, retention_notes)
select
  tenant_id, id, now(), @by, @reason,
  code, name, parent_id, retention_start_event,
  retention_active_days, retention_active_months, retention_active_years,
  retention_archive_days, retention_archive_months, retention_archive_years,
  final_destination, requires_digital_signature, is_confidential, is_active, retention_notes
from ged.classification_plan
where tenant_id=@tenant_id and id=@id;
";
            await conn.ExecuteAsync(new CommandDefinition(hist, new
            {
                tenant_id = tenantId,
                id,
                by = userId,
                reason = "CREATE"
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            _ = await _audit.WriteAsync(tenantId, userId, "CREATE", "classification_plan", id,
                "Classe criada (PCD/TTD)", null, null, new { vm.Code, vm.Name }, ct);

            return Result<Guid>.Ok(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassificationPlanCommands.CreateAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("PCD", "Falha ao criar classe.");
        }
    }

    public async Task<Result> UpdateAsync(Guid tenantId, Guid id, Guid? userId, ClassificationPlanUpdateVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (id == Guid.Empty) return Result.Fail("ID", "Id inválido.");
            if (vm is null) return Result.Fail("VM", "Dados inválidos.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string upd = @"
update ged.classification_plan
set code=@code,
    name=@name,
    description=@description,
    parent_id=@parent_id,
    retention_start_event=@retention_start_event::ged.retention_start_event,
    retention_active_days=@rad,
    retention_active_months=@ram,
    retention_active_years=@ray,
    retention_archive_days=@rrd,
    retention_archive_months=@rrm,
    retention_archive_years=@rry,
    final_destination=@final_destination::ged.final_destination,
    requires_digital_signature=@reqsig,
    is_confidential=@conf,
    is_active=@active,
    updated_at=now(),
    updated_by=@by,
    retention_notes=@rnotes
where tenant_id=@tenant_id and id=@id and is_active=true;
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
            {
                tenant_id = tenantId,
                id,
                code = vm.Code.Trim(),
                name = vm.Name.Trim(),
                description = vm.Description,
                parent_id = vm.ParentId,
                retention_start_event = vm.RetentionStartEvent,
                rad = vm.RetentionActiveDays,
                ram = vm.RetentionActiveMonths,
                ray = vm.RetentionActiveYears,
                rrd = vm.RetentionArchiveDays,
                rrm = vm.RetentionArchiveMonths,
                rry = vm.RetentionArchiveYears,
                final_destination = vm.FinalDestination,
                reqsig = vm.RequiresDigitalSignature,
                conf = vm.IsConfidential,
                active = vm.IsActive,
                by = userId,
                rnotes = vm.RetentionNotes
            }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Classe não encontrada.");
            }

            // history snapshot
            const string hist = @"
insert into ged.classification_plan_history
(tenant_id, classification_id, changed_at, changed_by, change_reason,
 code, name, parent_id, retention_start_event,
 retention_active_days, retention_active_months, retention_active_years,
 retention_archive_days, retention_archive_months, retention_archive_years,
 final_destination, requires_digital_signature, is_confidential, is_active, retention_notes)
select
  tenant_id, id, now(), @by, @reason,
  code, name, parent_id, retention_start_event,
  retention_active_days, retention_active_months, retention_active_years,
  retention_archive_days, retention_archive_months, retention_archive_years,
  final_destination, requires_digital_signature, is_confidential, is_active, retention_notes
from ged.classification_plan
where tenant_id=@tenant_id and id=@id;
";
            await conn.ExecuteAsync(new CommandDefinition(hist, new
            {
                tenant_id = tenantId,
                id,
                by = userId,
                reason = "UPDATE"
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            _ = await _audit.WriteAsync(tenantId, userId, "UPDATE", "classification_plan", id,
                "Classe atualizada (PCD/TTD)", null, null, new { vm.Code, vm.Name }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassificationPlanCommands.UpdateAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return Result.Fail("PCD", "Falha ao atualizar classe.");
        }
    }

    // ✅ Item 2: mover classe/código preservando subtree + histórico
    public async Task<Result> MoveAsync(Guid tenantId, Guid? userId, ClassificationPlanMoveVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (vm is null || vm.Id == Guid.Empty) return Result.Fail("ID", "Id inválido.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // pega código antigo
            const string getOld = @"select code from ged.classification_plan where tenant_id=@tenant_id and id=@id;";
            var oldCode = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(getOld, new { tenant_id = tenantId, id = vm.Id }, transaction: tx, cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(oldCode))
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Classe não encontrada.");
            }

            var newCode = string.IsNullOrWhiteSpace(vm.NewCode) ? oldCode! : vm.NewCode!.Trim();

            // atualiza parent e código do nó
            const string updNode = @"
update ged.classification_plan
set parent_id=@parent_id,
    code=@new_code,
    updated_at=now(),
    updated_by=@by
where tenant_id=@tenant_id and id=@id;
";
            await conn.ExecuteAsync(new CommandDefinition(updNode, new
            {
                tenant_id = tenantId,
                id = vm.Id,
                parent_id = vm.NewParentId,
                new_code = newCode,
                by = userId
            }, transaction: tx, cancellationToken: ct));

            // se mudou código, atualiza códigos descendentes (prefix replacement)
            if (!string.Equals(oldCode, newCode, StringComparison.OrdinalIgnoreCase))
            {
                const string updChildren = @"
with recursive tree as (
  select id, code
  from ged.classification_plan
  where tenant_id=@tenant_id and id=@root_id
  union all
  select c.id, c.code
  from ged.classification_plan c
  join tree t on c.parent_id = t.id
  where c.tenant_id=@tenant_id
)
update ged.classification_plan cp
set code = regexp_replace(cp.code, ('^' || @old_prefix), @new_prefix),
    updated_at=now(),
    updated_by=@by
from tree
where cp.tenant_id=@tenant_id
  and cp.id=tree.id
  and cp.id<>@root_id
  and cp.code ~ ('^' || @old_prefix);
";
                await conn.ExecuteAsync(new CommandDefinition(updChildren, new
                {
                    tenant_id = tenantId,
                    root_id = vm.Id,
                    old_prefix = oldCode,
                    new_prefix = newCode,
                    by = userId
                }, transaction: tx, cancellationToken: ct));
            }

            // history snapshot do nó + subtree (Item 2 exige histórico)
            const string histAll = @"
with recursive tree as (
  select id
  from ged.classification_plan
  where tenant_id=@tenant_id and id=@root_id
  union all
  select c.id
  from ged.classification_plan c
  join tree t on c.parent_id=t.id
  where c.tenant_id=@tenant_id
)
insert into ged.classification_plan_history
(tenant_id, classification_id, changed_at, changed_by, change_reason,
 code, name, parent_id, retention_start_event,
 retention_active_days, retention_active_months, retention_active_years,
 retention_archive_days, retention_archive_months, retention_archive_years,
 final_destination, requires_digital_signature, is_confidential, is_active, retention_notes)
select
  cp.tenant_id, cp.id, now(), @by, @reason,
  cp.code, cp.name, cp.parent_id, cp.retention_start_event,
  cp.retention_active_days, cp.retention_active_months, cp.retention_active_years,
  cp.retention_archive_days, cp.retention_archive_months, cp.retention_archive_years,
  cp.final_destination, cp.requires_digital_signature, cp.is_confidential, cp.is_active, cp.retention_notes
from ged.classification_plan cp
join tree t on t.id=cp.id
where cp.tenant_id=@tenant_id;
";
            await conn.ExecuteAsync(new CommandDefinition(histAll, new
            {
                tenant_id = tenantId,
                root_id = vm.Id,
                by = userId,
                reason = string.IsNullOrWhiteSpace(vm.Reason) ? "MOVE_CODE" : vm.Reason
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            _ = await _audit.WriteAsync(tenantId, userId, "UPDATE", "classification_plan", vm.Id,
                "Movimentação de código/classe (subárvore preservada)", null, null,
                new { oldCode, newCode, vm.NewParentId, vm.Reason }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassificationPlanCommands.MoveAsync failed. Tenant={Tenant}", tenantId);
            return Result.Fail("PCD", "Falha ao mover código/classe.");
        }
    }

    // ✅ Item 4: publicar versão (snapshot) do PCD/TTD inteiro
    public async Task<Result<Guid>> PublishVersionAsync(Guid tenantId, Guid? userId, PublishClassificationPlanVersionVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm is null || string.IsNullOrWhiteSpace(vm.Title)) return Result<Guid>.Fail("TITLE", "Título é obrigatório.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string nextNo = @"select coalesce(max(version_no),0)+1 from ged.classification_plan_version where tenant_id=@tenant_id;";
            var versionNo = await conn.ExecuteScalarAsync<int>(new CommandDefinition(nextNo, new { tenant_id = tenantId }, transaction: tx, cancellationToken: ct));

            const string insVer = @"
insert into ged.classification_plan_version
(id, tenant_id, version_no, title, notes, published_at, published_by)
values (gen_random_uuid(), @tenant_id, @no, @title, @notes, now(), @by)
returning id;
";
            var versionId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(insVer, new
            {
                tenant_id = tenantId,
                no = versionNo,
                title = vm.Title.Trim(),
                notes = vm.Notes,
                by = userId
            }, transaction: tx, cancellationToken: ct));

            // snapshot items (toda a árvore)
            const string insItems = @"
insert into ged.classification_plan_version_item
(tenant_id, version_id, classification_id, code, name, description, parent_code,
 retention_start_event,
 retention_active_days, retention_active_months, retention_active_years,
 retention_archive_days, retention_archive_months, retention_archive_years,
 final_destination, requires_digital_signature, is_confidential, is_active, retention_notes)
select
  cp.tenant_id,
  @version_id,
  cp.id,
  cp.code,
  cp.name,
  cp.description,
  p.code as parent_code,
  cp.retention_start_event,
  cp.retention_active_days, cp.retention_active_months, cp.retention_active_years,
  cp.retention_archive_days, cp.retention_archive_months, cp.retention_archive_years,
  cp.final_destination,
  cp.requires_digital_signature,
  cp.is_confidential,
  cp.is_active,
  cp.retention_notes
from ged.classification_plan cp
left join ged.classification_plan p on p.tenant_id=cp.tenant_id and p.id=cp.parent_id
where cp.tenant_id=@tenant_id
order by cp.code;
";
            await conn.ExecuteAsync(new CommandDefinition(insItems, new { tenant_id = tenantId, version_id = versionId }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            _ = await _audit.WriteAsync(tenantId, userId, "VERSION_CREATE", "classification_plan_version", versionId,
                "Publicação de versão do PCD/TTD", null, null, new { versionNo, vm.Title }, ct);

            return Result<Guid>.Ok(versionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassificationPlanCommands.PublishVersionAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("PCDVER", "Falha ao publicar versão.");
        }
    }
}