using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.ClassificationPlans;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Instruments;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.ClassificationPlans;

public sealed class ClassificationPlanRepository : IClassificationPlanRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ClassificationPlanRepository> _logger;
    private readonly IAuditWriter _audit;

    public ClassificationPlanRepository(
      IDbConnectionFactory db,
      IAuditWriter audit,
      ILogger<ClassificationPlanRepository> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ClassificationNodeRow>> ListTreeAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
select
  id as Id,
  parent_id as ParentId,
  code as Code,
  name as Name,
  is_active as IsActive,
  is_confidential as IsConfidential,
  requires_digital_signature as RequiresDigitalSignature,
  retention_start_event::text as RetentionStartEvent,
  retention_active_days as RetActiveDays,
  retention_active_months as RetActiveMonths,
  retention_active_years as RetActiveYears,
  retention_archive_days as RetArchiveDays,
  retention_archive_months as RetArchiveMonths,
  retention_archive_years as RetArchiveYears,
  final_destination::text as FinalDestination,
  retention_notes as RetentionNotes
from ged.classification_plan
where tenant_id = @tenantId
order by code;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<ClassificationNodeRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListTreeAsync failed. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<ClassificationEditVM?> GetAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        const string sql = @"
select
  id as Id,
  parent_id as ParentId,
  code as Code,
  name as Name,
  description as Description,
  retention_start_event::text as RetentionStartEvent,
  retention_active_days as RetentionActiveDays,
  retention_active_months as RetentionActiveMonths,
  retention_active_years as RetentionActiveYears,
  retention_archive_days as RetentionArchiveDays,
  retention_archive_months as RetentionArchiveMonths,
  retention_archive_years as RetentionArchiveYears,
  final_destination::text as FinalDestination,
  requires_digital_signature as RequiresDigitalSignature,
  is_confidential as IsConfidential,
  is_active as IsActive,
  retention_notes as RetentionNotes
from ged.classification_plan
where tenant_id=@tenantId and id=@id
limit 1;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<ClassificationEditVM>(
                new CommandDefinition(sql, new { tenantId, id }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAsync failed. Tenant={TenantId} Id={Id}", tenantId, id);
            throw;
        }
    }

    public async Task<Guid> UpsertAsync(Guid tenantId, Guid userId, ClassificationEditVM vm, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vm.Code)) throw new ArgumentException("Código é obrigatório.");
        if (string.IsNullOrWhiteSpace(vm.Name)) throw new ArgumentException("Nome é obrigatório.");

        var id = vm.Id ?? Guid.NewGuid();

        const string sql = @"
insert into ged.classification_plan (
  id, tenant_id, code, name, description, parent_id,
  retention_start_event,
  retention_active_days, retention_active_months, retention_active_years,
  retention_archive_days, retention_archive_months, retention_archive_years,
  final_destination, requires_digital_signature, is_confidential, is_active,
  created_at, created_by, updated_at, updated_by, retention_notes
)
values (
  @id, @tenantId, @code, @name, @description, @parentId,
  @retentionStartEvent::ged.retention_start_event,
  @retActiveDays, @retActiveMonths, @retActiveYears,
  @retArchiveDays, @retArchiveMonths, @retArchiveYears,
  @finalDestination::ged.final_destination,
  @requiresDigitalSignature, @isConfidential, @isActive,
  now(), @userId, now(), @userId, @retentionNotes
)
on conflict (id) do update set
  code = excluded.code,
  name = excluded.name,
  description = excluded.description,
  parent_id = excluded.parent_id,
  retention_start_event = excluded.retention_start_event,
  retention_active_days = excluded.retention_active_days,
  retention_active_months = excluded.retention_active_months,
  retention_active_years = excluded.retention_active_years,
  retention_archive_days = excluded.retention_archive_days,
  retention_archive_months = excluded.retention_archive_months,
  retention_archive_years = excluded.retention_archive_years,
  final_destination = excluded.final_destination,
  requires_digital_signature = excluded.requires_digital_signature,
  is_confidential = excluded.is_confidential,
  is_active = excluded.is_active,
  updated_at = now(),
  updated_by = @userId,
  retention_notes = excluded.retention_notes;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                id,
                tenantId,
                code = vm.Code.Trim(),
                name = vm.Name.Trim(),
                description = vm.Description,
                parentId = vm.ParentId,
                retentionStartEvent = vm.RetentionStartEvent,
                retActiveDays = vm.RetentionActiveDays,
                retActiveMonths = vm.RetentionActiveMonths,
                retActiveYears = vm.RetentionActiveYears,
                retArchiveDays = vm.RetentionArchiveDays,
                retArchiveMonths = vm.RetentionArchiveMonths,
                retArchiveYears = vm.RetentionArchiveYears,
                finalDestination = vm.FinalDestination,
                requiresDigitalSignature = vm.RequiresDigitalSignature,
                isConfidential = vm.IsConfidential,
                isActive = vm.IsActive,
                retentionNotes = vm.RetentionNotes,
                userId
            }, cancellationToken: ct));

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpsertAsync failed. Tenant={TenantId} Id={Id}", tenantId, id);
            throw;
        }
    }

    public async Task<Result> MoveAsync(Guid tenantId, Guid userId, Guid id, Guid? newParentId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (userId == Guid.Empty) return Result.Fail("USER", "Usuário inválido.");
            if (id == Guid.Empty) return Result.Fail("ID", "Id inválido.");
            if (newParentId.HasValue && newParentId.Value == id)
                return Result.Fail("CYCLE", "Movimentação inválida: destino não pode ser o próprio código.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // Nó origem
            const string getNode = @"
select id, code, parent_id as ParentId, is_active as IsActive
from ged.classification_plan
where tenant_id=@tenant_id and id=@id;
";
            var node = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Code, Guid? ParentId, bool IsActive)>(
                new CommandDefinition(getNode, new { tenant_id = tenantId, id }, transaction: tx, cancellationToken: ct));

            if (node.Id == Guid.Empty || string.IsNullOrWhiteSpace(node.Code))
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Classe não encontrada.");
            }

            if (!node.IsActive)
            {
                tx.Rollback();
                return Result.Fail("INACTIVE", "Classe está inativa e não pode ser movimentada.");
            }

            // Valida pai destino
            if (newParentId.HasValue)
            {
                const string parentOk = @"
select is_active
from ged.classification_plan
where tenant_id=@tenant_id and id=@pid;
";
                var parentIsActive = await conn.ExecuteScalarAsync<bool?>(
                    new CommandDefinition(parentOk, new { tenant_id = tenantId, pid = newParentId.Value }, transaction: tx, cancellationToken: ct));

                if (parentIsActive is null)
                {
                    tx.Rollback();
                    return Result.Fail("PARENT_NOTFOUND", "Classe destino não encontrada.");
                }
                if (parentIsActive != true)
                {
                    tx.Rollback();
                    return Result.Fail("PARENT_INACTIVE", "Classe destino está inativa.");
                }

                // Anti-ciclo
                const string isDescendant = @"
with recursive tree as (
  select id
  from ged.classification_plan
  where tenant_id=@tenant_id and parent_id=@root_id
  union all
  select c.id
  from ged.classification_plan c
  join tree t on c.parent_id = t.id
  where c.tenant_id=@tenant_id
)
select 1
from tree
where id=@new_parent_id
limit 1;
";
                var cycle = await conn.ExecuteScalarAsync<int?>(
                    new CommandDefinition(isDescendant, new
                    {
                        tenant_id = tenantId,
                        root_id = id,
                        new_parent_id = newParentId.Value
                    }, transaction: tx, cancellationToken: ct));

                if (cycle.HasValue)
                {
                    tx.Rollback();
                    return Result.Fail("CYCLE", "Movimentação inválida: destino é descendente do código movimentado (ciclo).");
                }
            }

            // Atualiza parent
            const string upd = @"
update ged.classification_plan
set parent_id=@parent_id,
    updated_at=now(),
    updated_by=@by
where tenant_id=@tenant_id and id=@id and is_active=true;
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(upd, new
            {
                tenant_id = tenantId,
                id,
                parent_id = newParentId,
                by = userId
            }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Classe não encontrada (ou inativa).");
            }

            // History snapshot (subárvore inteira)
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
  cp.tenant_id, cp.id, now(), @by, 'MOVE_PARENT',
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
                root_id = id,
                by = userId
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            _ = await _audit.WriteAsync(
                tenantId, userId,
                "UPDATE", "classification_plan", id,
                "Movimentação de classe/código (Item 2 - PCD/TTD)",
                null, null,
                new { newParentId },
                ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassificationPlanRepository.MoveAsync failed. Tenant={Tenant} Id={Id}", tenantId, id);
            return Result.Fail("PCD", "Falha ao mover classe/código.");
        }
    }
    public async Task<IReadOnlyList<ClassificationVersionRow>> ListVersionsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
select id as Id, version_no as VersionNo, title as Title, published_at as PublishedAt, published_by as PublishedBy
from ged.classification_plan_version
where tenant_id=@tenantId
order by version_no desc;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var list = await conn.QueryAsync<ClassificationVersionRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
            return list.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListVersionsAsync failed. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<Guid> PublishVersionAsync(Guid tenantId, Guid userId, string title, string? notes, CancellationToken ct)
    {
        var versionId = Guid.NewGuid();

        const string sqlNext = @"select coalesce(max(version_no), 0) + 1 from ged.classification_plan_version where tenant_id=@tenantId;";
        const string sqlInsert = @"
insert into ged.classification_plan_version(id, tenant_id, version_no, title, notes, published_at, published_by)
values (@id, @tenantId, @versionNo, @title, @notes, now(), @userId);";

        const string sqlSnapshot = @"
insert into ged.classification_plan_version_item(
  tenant_id, version_id, classification_id,
  code, name, description,
  parent_code,
  retention_start_event,
  retention_active_days, retention_active_months, retention_active_years,
  retention_archive_days, retention_archive_months, retention_archive_years,
  final_destination,
  requires_digital_signature,
  is_confidential,
  is_active,
  retention_notes
)
select
  c.tenant_id,
  @versionId,
  c.id,
  c.code,
  c.name,
  c.description,
  p.code as parent_code,
  c.retention_start_event::text,
  c.retention_active_days, c.retention_active_months, c.retention_active_years,
  c.retention_archive_days, c.retention_archive_months, c.retention_archive_years,
  c.final_destination::text,
  c.requires_digital_signature,
  c.is_confidential,
  c.is_active,
  c.retention_notes
from ged.classification_plan c
left join ged.classification_plan p on p.tenant_id=c.tenant_id and p.id=c.parent_id
where c.tenant_id=@tenantId;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var versionNo = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sqlNext, new { tenantId }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(sqlInsert, new { id = versionId, tenantId, versionNo, title, notes, userId }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(sqlSnapshot, new { tenantId, versionId }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return versionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PublishVersionAsync failed. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<ClassificationVersionDetailsVM?> GetVersionAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        const string sql = @"
select id as Id, version_no as VersionNo, title as Title, notes as Notes, published_at as PublishedAt, published_by as PublishedBy
from ged.classification_plan_version
where tenant_id=@tenantId and id=@versionId
limit 1;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<ClassificationVersionDetailsVM>(new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetVersionAsync failed. Tenant={TenantId} Version={VersionId}", tenantId, versionId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ClassificationVersionItemRow>> ListVersionItemsAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        const string sql = @"
select
  id as Id,
  classification_id as ClassificationId,
  code as Code,
  name as Name,
  description as Description,
  parent_code as ParentCode,
  retention_start_event as RetentionStartEvent,
  retention_active_days as RetentionActiveDays,
  retention_active_months as RetentionActiveMonths,
  retention_active_years as RetentionActiveYears,
  retention_archive_days as RetentionArchiveDays,
  retention_archive_months as RetentionArchiveMonths,
  retention_archive_years as RetentionArchiveYears,
  final_destination as FinalDestination,
  requires_digital_signature as RequiresDigitalSignature,
  is_confidential as IsConfidential,
  is_active as IsActive,
  retention_notes as RetentionNotes
from ged.classification_plan_version_item
where tenant_id=@tenantId and version_id=@versionId
order by code;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var list = await conn.QueryAsync<ClassificationVersionItemRow>(new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
            return list.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListVersionItemsAsync failed. Tenant={TenantId} Version={VersionId}", tenantId, versionId);
            throw;
        }
    }
}