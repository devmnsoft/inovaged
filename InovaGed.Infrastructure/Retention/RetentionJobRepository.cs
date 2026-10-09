using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Retention;

public sealed class RetentionJobRepository : IRetentionJobRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<RetentionJobRepository> _logger;

    public RetentionJobRepository(IDbConnectionFactory db, ILogger<RetentionJobRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> RecalculateAsync(Guid tenantId, int dueSoonDays, CancellationToken ct)
    {
        const string sql = @"
with base as (
  select
    d.id,
    d.tenant_id,
    d.created_at,
    d.archived_at,
    d.closed_at,
    d.classification_id,
    d.retention_hold,
    coalesce(pvi.retention_start_event::text, c.retention_start_event::text) as start_event,
    coalesce(pvi.final_destination::text, c.final_destination::text) as final_dest,
    coalesce(pvi.retention_active_days, c.retention_active_days, 0) as retention_active_days,
    coalesce(pvi.retention_active_months, c.retention_active_months, 0) as retention_active_months,
    coalesce(pvi.retention_active_years, c.retention_active_years, 0) as retention_active_years,
    coalesce(pvi.retention_archive_days, c.retention_archive_days, 0) as retention_archive_days,
    coalesce(pvi.retention_archive_months, c.retention_archive_months, 0) as retention_archive_months,
    coalesce(pvi.retention_archive_years, c.retention_archive_years, 0) as retention_archive_years
  from ged.document d
  left join ged.classification_plan c
    on c.tenant_id = d.tenant_id
   and c.id = d.classification_id
  left join ged.classification_plan_version_item pvi
    on pvi.tenant_id = d.tenant_id
   and pvi.version_id = d.classification_version_id
   and pvi.classification_id = d.classification_id
  where d.tenant_id = @tenantId
),
calc as (
  select
    id,
    tenant_id,
    classification_id,
    retention_hold,
    final_dest,
    case
      when classification_id is null then null
      when start_event = 'ARQUIVAMENTO' then archived_at
      when start_event = 'ENCERRAMENTO' then closed_at
      when start_event = 'ABERTURA' then created_at
      else created_at
    end as basis_at,
    case
      when classification_id is null then null
      when final_dest = 'GUARDA_PERMANENTE' then null
      when start_event = 'ARQUIVAMENTO' and archived_at is null then null
      when start_event = 'ENCERRAMENTO' and closed_at is null then null
      else
        (
          case
            when start_event = 'ARQUIVAMENTO' then archived_at
            when start_event = 'ENCERRAMENTO' then closed_at
            when start_event = 'ABERTURA' then created_at
            else created_at
          end
          + make_interval(days => (retention_active_days + retention_archive_days))
          + make_interval(months => (retention_active_months + retention_archive_months))
          + make_interval(years => (retention_active_years + retention_archive_years))
        )
    end as due_at
  from base
)
update ged.document d
set
  retention_basis_at = c.basis_at,
  retention_due_at = c.due_at,
  retention_status = case
    when c.classification_id is null then null
    when coalesce(c.retention_hold, false) = true then 'HOLD'
    when c.final_dest = 'GUARDA_PERMANENTE' then 'PERMANENT_RECORD'
    when c.basis_at is null then 'EVENT_PENDING'
    when c.due_at is null then null
    when c.due_at < now() then 'OVERDUE'
    when c.due_at <= (now() + make_interval(days => @dueSoonDays)) then 'DUE_SOON'
    else 'OK'
  end
from calc c
where d.tenant_id = c.tenant_id
  and d.id = c.id;
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, dueSoonDays }, cancellationToken: ct));
            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention recalculation failed. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<RetentionDashboardVM> GetDashboardAsync(Guid tenantId, int dueSoonDays, CancellationToken ct)
    {
        const string sql = @"
select
  (select count(1) from ged.document d where d.tenant_id=@tenantId and d.classification_id is not null) as TotalClassified,
  (select count(1) from ged.document d where d.tenant_id=@tenantId and d.retention_due_at is not null and d.retention_due_at <= (now() + make_interval(days => @dueSoonDays)) and d.retention_due_at >= now()) as DueSoon,
  (select count(1) from ged.document d where d.tenant_id=@tenantId and d.retention_due_at is not null and d.retention_due_at < now()) as Overdue,
  (select count(1) from ged.document d where d.tenant_id=@tenantId and d.classification_id is null) as WithoutClassification;
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.QuerySingleAsync<RetentionDashboardVM>(
                new CommandDefinition(sql, new { tenantId, dueSoonDays }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDashboardAsync failed. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<int> RecalculateOneAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(RecalculateOneSql, new { tenantId, documentId, dueSoonDays }, cancellationToken: ct));
    }

    public Task<int> RecalculateOneAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
        => connection.ExecuteAsync(new CommandDefinition(RecalculateOneSql, new { tenantId, documentId, dueSoonDays }, transaction, cancellationToken: ct));

    public async Task<Guid> EnqueueRecalculateAsync(Guid tenantId, Guid documentId, string reason, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await EnqueueRecalculateAsync(conn, null!, tenantId, documentId, reason, ct);
    }

    public async Task<Guid> EnqueueRecalculateAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid documentId, string reason, CancellationToken ct)
    {
        var newId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_retention_recalc_pending
  (id, tenant_id, document_id, application_id, reason, attempts, next_attempt_at)
values
  (@id, @tenantId, @documentId, null, @reason, 0, now())
""", new
        {
            id = newId,
            tenantId,
            documentId,
            reason = string.IsNullOrWhiteSpace(reason) ? "RETENTION_RECALC" : reason.Trim()
        }, transaction, cancellationToken: ct));
        return newId;
    }

    public async Task<bool> ResolvePendingRecalcAsync(Guid tenantId, Guid pendingId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await ResolvePendingRecalcAsync(conn, null!, tenantId, pendingId, ct);
    }

    public async Task<bool> ResolvePendingRecalcAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid pendingId, CancellationToken ct)
    {
        var rows = await connection.ExecuteAsync(new CommandDefinition("""
update ged.ai_retention_recalc_pending
set resolved_at=now(), last_error=null, claimed_at=null, claim_token=null
where tenant_id=@tenantId and id=@pendingId and resolved_at is null
""", new { tenantId, pendingId }, transaction, cancellationToken: ct));
        return rows == 1;
    }

    private const string RecalculateOneSql = @"
with base as (
  select
    d.id,
    d.tenant_id,
    d.created_at,
    d.archived_at,
    d.closed_at,
    d.classification_id,
    d.retention_hold,
    coalesce(pvi.retention_start_event::text, c.retention_start_event::text) as start_event,
    coalesce(pvi.final_destination::text, c.final_destination::text) as final_dest,
    coalesce(pvi.retention_active_days, c.retention_active_days, 0) as retention_active_days,
    coalesce(pvi.retention_active_months, c.retention_active_months, 0) as retention_active_months,
    coalesce(pvi.retention_active_years, c.retention_active_years, 0) as retention_active_years,
    coalesce(pvi.retention_archive_days, c.retention_archive_days, 0) as retention_archive_days,
    coalesce(pvi.retention_archive_months, c.retention_archive_months, 0) as retention_archive_months,
    coalesce(pvi.retention_archive_years, c.retention_archive_years, 0) as retention_archive_years
  from ged.document d
  left join ged.classification_plan c
    on c.tenant_id = d.tenant_id
   and c.id = d.classification_id
  left join ged.classification_plan_version_item pvi
    on pvi.tenant_id = d.tenant_id
   and pvi.version_id = d.classification_version_id
   and pvi.classification_id = d.classification_id
  where d.tenant_id = @tenantId
    and d.id = @documentId
),
calc as (
  select
    id,
    tenant_id,
    classification_id,
    retention_hold,
    final_dest,
    case
      when classification_id is null then null
      when start_event = 'ARQUIVAMENTO' then archived_at
      when start_event = 'ENCERRAMENTO' then closed_at
      when start_event = 'ABERTURA' then created_at
      else created_at
    end as basis_at,
    case
      when classification_id is null then null
      when final_dest = 'GUARDA_PERMANENTE' then null
      when start_event = 'ARQUIVAMENTO' and archived_at is null then null
      when start_event = 'ENCERRAMENTO' and closed_at is null then null
      else
        (
          case
            when start_event = 'ARQUIVAMENTO' then archived_at
            when start_event = 'ENCERRAMENTO' then closed_at
            when start_event = 'ABERTURA' then created_at
            else created_at
          end
          + make_interval(days => (retention_active_days + retention_archive_days))
          + make_interval(months => (retention_active_months + retention_archive_months))
          + make_interval(years => (retention_active_years + retention_archive_years))
        )
    end as due_at
  from base
)
update ged.document d
set
  retention_basis_at = c.basis_at,
  retention_due_at   = c.due_at,
  retention_status   = case
    when c.classification_id is null then null
    when coalesce(c.retention_hold, false) = true then 'HOLD'
    when c.final_dest = 'GUARDA_PERMANENTE' then 'PERMANENT_RECORD'
    when c.basis_at is null then 'EVENT_PENDING'
    when c.due_at is null then null
    when c.due_at < now() then 'OVERDUE'
    when c.due_at <= (now() + make_interval(days => @dueSoonDays)) then 'DUE_SOON'
    else 'OK'
  end
from calc c
where d.tenant_id = c.tenant_id
  and d.id = c.id;
";
}
