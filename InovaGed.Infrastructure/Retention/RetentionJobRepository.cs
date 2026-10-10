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

    private static string BuildRetentionCte(string docFilter) => $@"
with base as (
  select
    d.id,
    d.tenant_id,
    d.created_at,
    d.archived_at,
    d.closed_at,
    d.classification_id,
    d.retention_hold,
    d.retention_due_at as persisted_due_at,
    d.retention_status as persisted_status,
    d.retention_basis_at as persisted_basis_at,
    case
      when d.classification_version_id is not null then 'VERSIONED_ITEM'
      when d.classification_id is not null then 'LEGACY_PLAN'
      else null
    end as rule_source,
    case
      when d.classification_version_id is not null then pvi.code
      else c.code
    end as classification_code,
    case
      when d.classification_version_id is not null then coalesce(pvi.name, pvi.title)
      else c.name
    end as classification_name,
    pv.version_no as plan_version_no,
    pv.title as plan_version_title,
    case
      when d.classification_version_id is not null then pvi.retention_start_event::text
      else c.retention_start_event::text
    end as start_event,
    case
      when d.classification_version_id is not null then pvi.final_destination::text
      else c.final_destination::text
    end as final_dest,
    case
      when d.classification_version_id is not null then pvi.retention_active_days
      else c.retention_active_days
    end as raw_active_days,
    case
      when d.classification_version_id is not null then pvi.retention_active_months
      else c.retention_active_months
    end as raw_active_months,
    case
      when d.classification_version_id is not null then pvi.retention_active_years
      else c.retention_active_years
    end as raw_active_years,
    case
      when d.classification_version_id is not null then pvi.retention_archive_days
      else c.retention_archive_days
    end as raw_archive_days,
    case
      when d.classification_version_id is not null then pvi.retention_archive_months
      else c.retention_archive_months
    end as raw_archive_months,
    case
      when d.classification_version_id is not null then pvi.retention_archive_years
      else c.retention_archive_years
    end as raw_archive_years
  from ged.document d
  left join ged.classification_plan c
    on c.tenant_id = d.tenant_id
   and c.id = d.classification_id
  left join ged.classification_plan_version pv
    on pv.tenant_id = d.tenant_id
   and pv.id = d.classification_version_id
  left join ged.classification_plan_version_item pvi
    on pvi.tenant_id = d.tenant_id
   and pvi.version_id = d.classification_version_id
   and pvi.classification_id = d.classification_id
  where d.tenant_id = @tenantId
    {docFilter}
),
calc as (
  select
    id,
    tenant_id,
    classification_id,
    retention_hold,
    classification_code,
    classification_name,
    plan_version_no,
    plan_version_title,
    rule_source,
    start_event,
    final_dest,
    raw_active_days,
    raw_active_months,
    raw_active_years,
    raw_archive_days,
    raw_archive_months,
    raw_archive_years,
    persisted_due_at,
    persisted_status,
    persisted_basis_at,
    (
      (final_dest is null and raw_active_days is null and raw_active_months is null and raw_active_years is null and raw_archive_days is null and raw_archive_months is null and raw_archive_years is null)
      or (final_dest is not null and final_dest <> 'GUARDA_PERMANENTE' and raw_active_days is null and raw_active_months is null and raw_active_years is null and raw_archive_days is null and raw_archive_months is null and raw_archive_years is null)
    ) as is_incomplete,
    (
      (raw_active_days is not null or raw_active_months is not null or raw_active_years is not null or raw_archive_days is not null or raw_archive_months is not null or raw_archive_years is not null)
      and coalesce(raw_active_days, 0) = 0 and coalesce(raw_active_months, 0) = 0 and coalesce(raw_active_years, 0) = 0
      and coalesce(raw_archive_days, 0) = 0 and coalesce(raw_archive_months, 0) = 0 and coalesce(raw_archive_years, 0) = 0
    ) as explicit_zero,
    case
      when classification_id is null then null
      when upper(coalesce(start_event, '')) in ('ABERTURA', 'CRIACAO') then created_at
      when upper(coalesce(start_event, '')) = 'ENCERRAMENTO' then closed_at
      when upper(coalesce(start_event, '')) = 'ARQUIVAMENTO' then archived_at
      else null
    end as basis_at
  from base
),
due_eval as (
  select
    c.*,
    case
      when c.classification_id is null then null
      when c.final_dest = 'GUARDA_PERMANENTE' then null
      when c.is_incomplete then null
      when c.basis_at is null then null
      else
        (
          c.basis_at
          + make_interval(years => (coalesce(c.raw_active_years, 0) + coalesce(c.raw_archive_years, 0)))
          + make_interval(months => (coalesce(c.raw_active_months, 0) + coalesce(c.raw_archive_months, 0)))
          + make_interval(days => (coalesce(c.raw_active_days, 0) + coalesce(c.raw_archive_days, 0)))
        )
    end as due_at
  from calc c
),
final_eval as (
  select
    d.*,
    case
      when d.classification_id is null then null
      when coalesce(d.retention_hold, false) = true then 'HOLD'
      when d.final_dest = 'GUARDA_PERMANENTE' then 'PERMANENT_RECORD'
      when d.is_incomplete then 'INCOMPLETE_RULE'
      when d.basis_at is null then 'EVENT_PENDING'
      when d.due_at is null then null
      when d.due_at < now() then 'OVERDUE'
      when d.due_at <= (now() + make_interval(days => @dueSoonDays)) then 'DUE_SOON'
      else 'OK'
    end as calculated_status
  from due_eval d
)
";

    private static string BuildRetentionUpdateSql(string docFilter) => $@"
{BuildRetentionCte(docFilter)}
update ged.document doc
set
  retention_basis_at = f.basis_at,
  retention_due_at   = f.due_at,
  retention_status   = f.calculated_status
from final_eval f
where doc.tenant_id = f.tenant_id
  and doc.id = f.id;
";

    public async Task<int> RecalculateAsync(Guid tenantId, int dueSoonDays, CancellationToken ct)
    {
        var sql = BuildRetentionUpdateSql(string.Empty);
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, dueSoonDays }, cancellationToken: ct));
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

    private static readonly string RecalculateOneSql = BuildRetentionUpdateSql("and d.id = @documentId");

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

    public async Task<RetentionCalculationMemory?> SimulateCalculationAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
    {
        var selectSql = $@"
{BuildRetentionCte("and d.id = @documentId")}
select
  f.id as DocumentId,
  f.classification_id as ClassificationId,
  f.classification_code as ClassificationCode,
  f.classification_name as ClassificationName,
  f.plan_version_no as PlanVersionNo,
  f.plan_version_title as PlanVersionTitle,
  f.rule_source as RuleSource,
  f.start_event as StartEvent,
  f.basis_at as BasisDate,
  (f.basis_at is null and not f.is_incomplete and f.final_dest <> 'GUARDA_PERMANENTE') as IsBasisEventPending,
  f.raw_active_days as RetentionActiveDays,
  f.raw_active_months as RetentionActiveMonths,
  f.raw_active_years as RetentionActiveYears,
  f.raw_archive_days as RetentionArchiveDays,
  f.raw_archive_months as RetentionArchiveMonths,
  f.raw_archive_years as RetentionArchiveYears,
  f.explicit_zero as HasExplicitZeroPeriod,
  f.is_incomplete as IsRuleIncomplete,
  f.final_dest as FinalDestination,
  (f.final_dest = 'GUARDA_PERMANENTE') as IsPermanentRecord,
  coalesce(f.retention_hold, false) as IsHoldActive,
  f.due_at as CalculatedDueAt,
  coalesce(f.calculated_status, 'SEM_CLASSIFICACAO') as CalculatedStatus,
  f.persisted_due_at as PersistedDueAt,
  f.persisted_status as PersistedStatus,
  f.persisted_basis_at as PersistedBasisAt
from final_eval f;
";

        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<SimulateResultRow>(
            new CommandDefinition(selectSql, new { tenantId, documentId, dueSoonDays }, cancellationToken: ct));

        if (row is null) return null;

        string formula;
        if (!row.ClassificationId.HasValue)
        {
            formula = "Documento sem classificação arquivística associada.";
        }
        else if (row.IsHoldActive)
        {
            formula = "Retenção suspensa por impedimento legal ou administrativo (HOLD ativo).";
        }
        else if (row.IsPermanentRecord)
        {
            formula = "Destinação final: Guarda Permanente. Documento preservado definitivamente sem expiração.";
        }
        else if (row.IsRuleIncomplete)
        {
            formula = "Regra de temporalidade incompleta: prazos e destinação final não definidos no plano vigente.";
        }
        else if (row.IsBasisEventPending)
        {
            formula = $"Evento gatilho pendente: {row.StartEvent ?? "NÃO_DEFINIDO"}. A contagem iniciará quando o evento for registrado no GED/Protocolo.";
        }
        else if (row.CalculatedDueAt.HasValue && row.BasisDate.HasValue)
        {
            formula = $"Data Base ({row.StartEvent}: {row.BasisDate:yyyy-MM-dd}) + Corrente ({(row.RetentionActiveYears ?? 0)}a {(row.RetentionActiveMonths ?? 0)}m {(row.RetentionActiveDays ?? 0)}d) + Intermediária ({(row.RetentionArchiveYears ?? 0)}a {(row.RetentionArchiveMonths ?? 0)}m {(row.RetentionArchiveDays ?? 0)}d) = Vencimento previsto em {row.CalculatedDueAt:yyyy-MM-dd} ({row.CalculatedStatus}).";
        }
        else
        {
            formula = "Sem dados suficientes para cálculo de temporalidade.";
        }

        return new RetentionCalculationMemory
        {
            DocumentId = row.DocumentId,
            ClassificationId = row.ClassificationId,
            ClassificationCode = row.ClassificationCode,
            ClassificationName = row.ClassificationName,
            PlanVersionNo = row.PlanVersionNo,
            PlanVersionTitle = row.PlanVersionTitle,
            RuleSource = row.RuleSource,
            StartEvent = row.StartEvent,
            BasisDate = row.BasisDate,
            IsBasisEventPending = row.IsBasisEventPending,
            RetentionActiveDays = row.RetentionActiveDays,
            RetentionActiveMonths = row.RetentionActiveMonths,
            RetentionActiveYears = row.RetentionActiveYears,
            RetentionArchiveDays = row.RetentionArchiveDays,
            RetentionArchiveMonths = row.RetentionArchiveMonths,
            RetentionArchiveYears = row.RetentionArchiveYears,
            HasExplicitZeroPeriod = row.HasExplicitZeroPeriod,
            IsRuleIncomplete = row.IsRuleIncomplete,
            FinalDestination = row.FinalDestination,
            IsPermanentRecord = row.IsPermanentRecord,
            IsHoldActive = row.IsHoldActive,
            CalculatedDueAt = row.CalculatedDueAt,
            CalculatedStatus = row.CalculatedStatus,
            FormulaText = formula,
            PersistedDueAt = row.PersistedDueAt,
            PersistedStatus = row.PersistedStatus,
            PersistedBasisAt = row.PersistedBasisAt
        };
    }

    private sealed class SimulateResultRow
    {
        public Guid DocumentId { get; set; }
        public Guid? ClassificationId { get; set; }
        public string? ClassificationCode { get; set; }
        public string? ClassificationName { get; set; }
        public int? PlanVersionNo { get; set; }
        public string? PlanVersionTitle { get; set; }
        public string? RuleSource { get; set; }
        public string? StartEvent { get; set; }
        public DateTime? BasisDate { get; set; }
        public bool IsBasisEventPending { get; set; }
        public int? RetentionActiveDays { get; set; }
        public int? RetentionActiveMonths { get; set; }
        public int? RetentionActiveYears { get; set; }
        public int? RetentionArchiveDays { get; set; }
        public int? RetentionArchiveMonths { get; set; }
        public int? RetentionArchiveYears { get; set; }
        public bool HasExplicitZeroPeriod { get; set; }
        public bool IsRuleIncomplete { get; set; }
        public string? FinalDestination { get; set; }
        public bool IsPermanentRecord { get; set; }
        public bool IsHoldActive { get; set; }
        public DateTime? CalculatedDueAt { get; set; }
        public string CalculatedStatus { get; set; } = "";
        public DateTime? PersistedDueAt { get; set; }
        public string? PersistedStatus { get; set; }
        public DateTime? PersistedBasisAt { get; set; }
    }
}
