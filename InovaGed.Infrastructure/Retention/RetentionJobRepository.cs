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
        // ✅ AJUSTE AQUI se o seu nome de tabela for diferente:
        // ged.document  + coluna classification_id
        // e ged.classification_plan com os campos de retenção

        const string sql = @"
with base as (
  select
    d.id,
    d.tenant_id,
    d.created_at,
    d.archived_at,
    d.closed_at,
    d.classification_id,
    c.retention_start_event::text as start_event,
    c.retention_active_days,
    c.retention_active_months,
    c.retention_active_years,
    c.retention_archive_days,
    c.retention_archive_months,
    c.retention_archive_years
  from ged.document d
  left join ged.classification_plan c
    on c.tenant_id = d.tenant_id
   and c.id = d.classification_id
  where d.tenant_id = @tenantId
),
calc as (
  select
    id,
    tenant_id,
    classification_id,
    case
      when classification_id is null then null
      when start_event = 'ARQUIVAMENTO' then coalesce(archived_at, created_at)
      when start_event = 'ENCERRAMENTO' then coalesce(closed_at, created_at)
      when start_event = 'ABERTURA' then created_at
      else created_at
    end as basis_at,
    case
      when classification_id is null then null
      else
        (
          case
            when start_event = 'ARQUIVAMENTO' then coalesce(archived_at, created_at)
            when start_event = 'ENCERRAMENTO' then coalesce(closed_at, created_at)
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
        // ✅ Ajuste se seu nome de tabela/colunas for diferente
        const string sql = @"
with base as (
  select
    d.id,
    d.tenant_id,
    d.created_at,
    d.archived_at,
    d.closed_at,
    d.classification_id,
    c.retention_start_event::text as start_event,
    c.retention_active_days,
    c.retention_active_months,
    c.retention_active_years,
    c.retention_archive_days,
    c.retention_archive_months,
    c.retention_archive_years
  from ged.document d
  left join ged.classification_plan c
    on c.tenant_id = d.tenant_id
   and c.id = d.classification_id
  where d.tenant_id = @tenantId
    and d.id = @documentId
),
calc as (
  select
    id,
    tenant_id,
    classification_id,
    case
      when classification_id is null then null
      when start_event = 'ARQUIVAMENTO' then coalesce(archived_at, created_at)
      when start_event = 'ENCERRAMENTO' then coalesce(closed_at, created_at)
      when start_event = 'ABERTURA' then created_at
      else created_at
    end as basis_at,
    case
      when classification_id is null then null
      else
        (
          case
            when start_event = 'ARQUIVAMENTO' then coalesce(archived_at, created_at)
            when start_event = 'ENCERRAMENTO' then coalesce(closed_at, created_at)
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
            return await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, dueSoonDays }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RecalculateOneAsync failed. Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            throw;
        }
    }
}