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
    coalesce(pvi.retention_active_days, c.retention_active_days) as raw_active_days,
    coalesce(pvi.retention_active_months, c.retention_active_months) as raw_active_months,
    coalesce(pvi.retention_active_years, c.retention_active_years) as raw_active_years,
    coalesce(pvi.retention_archive_days, c.retention_archive_days) as raw_archive_days,
    coalesce(pvi.retention_archive_months, c.retention_archive_months) as raw_archive_months,
    coalesce(pvi.retention_archive_years, c.retention_archive_years) as raw_archive_years,
    (coalesce(pvi.final_destination::text, c.final_destination::text) is null
     and coalesce(pvi.retention_active_days, c.retention_active_days) is null
     and coalesce(pvi.retention_active_months, c.retention_active_months) is null
     and coalesce(pvi.retention_active_years, c.retention_active_years) is null
     and coalesce(pvi.retention_archive_days, c.retention_archive_days) is null
     and coalesce(pvi.retention_archive_months, c.retention_archive_months) is null
     and coalesce(pvi.retention_archive_years, c.retention_archive_years) is null) as is_incomplete
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
    is_incomplete,
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
      when is_incomplete then null
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
          + make_interval(days => (coalesce(raw_active_days, 0) + coalesce(raw_archive_days, 0)))
          + make_interval(months => (coalesce(raw_active_months, 0) + coalesce(raw_archive_months, 0)))
          + make_interval(years => (coalesce(raw_active_years, 0) + coalesce(raw_archive_years, 0)))
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
    when c.is_incomplete then 'INCOMPLETE_RULE'
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
    coalesce(pvi.retention_active_days, c.retention_active_days) as raw_active_days,
    coalesce(pvi.retention_active_months, c.retention_active_months) as raw_active_months,
    coalesce(pvi.retention_active_years, c.retention_active_years) as raw_active_years,
    coalesce(pvi.retention_archive_days, c.retention_archive_days) as raw_archive_days,
    coalesce(pvi.retention_archive_months, c.retention_archive_months) as raw_archive_months,
    coalesce(pvi.retention_archive_years, c.retention_archive_years) as raw_archive_years,
    (coalesce(pvi.final_destination::text, c.final_destination::text) is null
     and coalesce(pvi.retention_active_days, c.retention_active_days) is null
     and coalesce(pvi.retention_active_months, c.retention_active_months) is null
     and coalesce(pvi.retention_active_years, c.retention_active_years) is null
     and coalesce(pvi.retention_archive_days, c.retention_archive_days) is null
     and coalesce(pvi.retention_archive_months, c.retention_archive_months) is null
     and coalesce(pvi.retention_archive_years, c.retention_archive_years) is null) as is_incomplete
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
    is_incomplete,
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
      when is_incomplete then null
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
          + make_interval(days => (coalesce(raw_active_days, 0) + coalesce(raw_archive_days, 0)))
          + make_interval(months => (coalesce(raw_active_months, 0) + coalesce(raw_archive_months, 0)))
          + make_interval(years => (coalesce(raw_active_years, 0) + coalesce(raw_archive_years, 0)))
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
    when c.is_incomplete then 'INCOMPLETE_RULE'
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

    public async Task<RetentionCalculationMemory?> SimulateCalculationAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
    {
        const string sql = @"
with base as (
  select
    d.id as DocumentId,
    d.created_at as CreatedAt,
    d.archived_at as ArchivedAt,
    d.closed_at as ClosedAt,
    d.classification_id as ClassificationId,
    d.retention_hold as RetentionHold,
    coalesce(pvi.code, c.code) as ClassificationCode,
    coalesce(pvi.name, c.name) as ClassificationName,
    pv.version_no as PlanVersionNo,
    pv.title as PlanVersionTitle,
    coalesce(pvi.retention_start_event::text, c.retention_start_event::text) as StartEvent,
    coalesce(pvi.final_destination::text, c.final_destination::text) as FinalDestination,
    coalesce(pvi.retention_active_days, c.retention_active_days) as RawActiveDays,
    coalesce(pvi.retention_active_months, c.retention_active_months) as RawActiveMonths,
    coalesce(pvi.retention_active_years, c.retention_active_years) as RawActiveYears,
    coalesce(pvi.retention_archive_days, c.retention_archive_days) as RawArchiveDays,
    coalesce(pvi.retention_archive_months, c.retention_archive_months) as RawArchiveMonths,
    coalesce(pvi.retention_archive_years, c.retention_archive_years) as RawArchiveYears
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
    and d.id = @documentId
)
select * from base;
";
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        if (row == null) return null;

        Guid? classId = row.classificationid;
        if (!classId.HasValue)
        {
            return new RetentionCalculationMemory
            {
                DocumentId = documentId,
                CalculatedStatus = "SEM_CLASSIFICACAO",
                FormulaText = "Documento sem classificação arquivística associada."
            };
        }

        string? startEvent = row.startevent;
        string? finalDest = row.finaldestination;
        DateTime? createdAt = row.createdat;
        DateTime? archivedAt = row.archivedat;
        DateTime? closedAt = row.closedat;
        bool hold = row.retentionhold ?? false;

        int? actDays = row.rawactivedays;
        int? actMonths = row.rawactivemonths;
        int? actYears = row.rawactiveyears;
        int? arcDays = row.rawarchivedays;
        int? arcMonths = row.rawarchivemonths;
        int? arcYears = row.rawarchiveyears;

        bool hasAnyPeriod = actDays.HasValue || actMonths.HasValue || actYears.HasValue || arcDays.HasValue || arcMonths.HasValue || arcYears.HasValue;
        bool isRuleIncomplete = string.IsNullOrWhiteSpace(finalDest) && !hasAnyPeriod;
        bool explicitZero = hasAnyPeriod && (actDays ?? 0) == 0 && (actMonths ?? 0) == 0 && (actYears ?? 0) == 0 && (arcDays ?? 0) == 0 && (arcMonths ?? 0) == 0 && (arcYears ?? 0) == 0;

        DateTime? basisDate = startEvent switch
        {
            "ARQUIVAMENTO" => archivedAt,
            "ENCERRAMENTO" => closedAt,
            "ABERTURA" => createdAt,
            _ => createdAt
        };

        bool eventPending = (startEvent == "ARQUIVAMENTO" && archivedAt == null) || (startEvent == "ENCERRAMENTO" && closedAt == null);
        bool isPermanent = string.Equals(finalDest, "GUARDA_PERMANENTE", StringComparison.OrdinalIgnoreCase);

        DateTime? dueAt = null;
        string status;
        string formula;

        if (hold)
        {
            status = "HOLD";
            formula = "Retenção suspensa por impedimento legal ou administrativo (HOLD ativo).";
        }
        else if (isPermanent)
        {
            status = "PERMANENT_RECORD";
            formula = "Destinação final: Guarda Permanente. Documento preservado definitivamente sem expiração.";
        }
        else if (isRuleIncomplete)
        {
            status = "INCOMPLETE_RULE";
            formula = "Regra de temporalidade incompleta: prazos e destinação final não definidos no plano vigente.";
        }
        else if (eventPending)
        {
            status = "EVENT_PENDING";
            formula = $"Evento gatilho pendente: {startEvent}. A contagem iniciará quando o evento for registrado no GED/Protocolo.";
        }
        else if (basisDate.HasValue)
        {
            var totalDays = (actDays ?? 0) + (arcDays ?? 0);
            var totalMonths = (actMonths ?? 0) + (arcMonths ?? 0);
            var totalYears = (actYears ?? 0) + (arcYears ?? 0);

            dueAt = basisDate.Value.AddDays(totalDays).AddMonths(totalMonths).AddYears(totalYears);
            var now = DateTime.UtcNow;

            if (dueAt < now)
                status = "OVERDUE";
            else if (dueAt <= now.AddDays(dueSoonDays))
                status = "DUE_SOON";
            else
                status = "OK";

            formula = $"Data Base ({startEvent}: {basisDate:yyyy-MM-dd}) + Corrente ({(actYears ?? 0)}a {(actMonths ?? 0)}m {(actDays ?? 0)}d) + Intermediária ({(arcYears ?? 0)}a {(arcMonths ?? 0)}m {(arcDays ?? 0)}d) = Vencimento em {dueAt:yyyy-MM-dd} ({status}).";
        }
        else
        {
            status = "SEM_CLASSIFICACAO";
            formula = "Sem dados suficientes para cálculo de temporalidade.";
        }

        return new RetentionCalculationMemory
        {
            DocumentId = documentId,
            ClassificationId = classId,
            ClassificationCode = row.classificationcode,
            ClassificationName = row.classificationname,
            PlanVersionNo = row.planversionno,
            PlanVersionTitle = row.planversiontitle,
            StartEvent = startEvent,
            BasisDate = basisDate,
            IsBasisEventPending = eventPending,
            RetentionActiveDays = actDays,
            RetentionActiveMonths = actMonths,
            RetentionActiveYears = actYears,
            RetentionArchiveDays = arcDays,
            RetentionArchiveMonths = arcMonths,
            RetentionArchiveYears = arcYears,
            HasExplicitZeroPeriod = explicitZero,
            IsRuleIncomplete = isRuleIncomplete,
            FinalDestination = finalDest,
            IsPermanentRecord = isPermanent,
            IsHoldActive = hold,
            CalculatedDueAt = dueAt,
            CalculatedStatus = status,
            FormulaText = formula
        };
    }
}
