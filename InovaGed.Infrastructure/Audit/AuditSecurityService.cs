using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Audit;

public sealed class AuditSecurityService : IAuditSecurityService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _auditWriter;

    public AuditSecurityService(IDbConnectionFactory db, IAuditWriter auditWriter)
    {
        _db = db;
        _auditWriter = auditWriter;
    }

    public async Task RegisterCriticalActionAsync(Guid tenantId, Guid? userId, string action, string entityName, string entityId, string summary, string? ipAddress, CancellationToken ct)
    {
        await _auditWriter.WriteAsync(tenantId, userId, action, entityName, entityId, summary, null, null, ipAddress, ct);
    }

    public async Task<AuditDashboardVM> GetDashboardAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var sql = @"
with base as (
    select *
    from ged.audit_log
    where tenant_id = @tenantId
      and event_time >= (now() at time zone 'utc') - interval '24 hour'
)
select
    count(*)::int as total_events,
    count(*) filter (where action::text = 'ACCESS_DENIED')::int as access_denied,
    count(*) filter (where entity_name ilike 'document%' and action::text in ('UPDATE','DELETE','UPLOAD'))::int as changed_documents,
    count(*) filter (where action::text in ('LOGIN','LOGOUT','MFA_CHALLENGE','MFA_SUCCESS','MFA_FAILED'))::int as auth_events
from base;";

        var row = await conn.QuerySingleAsync(sql, new { tenantId });

        var alerts = (await conn.QueryAsync<SuspiciousActivityAlertDto>(new CommandDefinition(@"
select
    event_time as EventTime,
    user_id as UserId,
    u.name as UserName,
    action::text as Action,
    entity_name as EntityName,
    summary as Summary,
    ip_address as IpAddress
from ged.audit_log a
left join ged.users u on u.tenant_id = a.tenant_id and u.id = a.user_id
where a.tenant_id = @tenantId
  and a.event_time >= (now() at time zone 'utc') - interval '24 hour'
  and (
      a.action::text = 'ACCESS_DENIED'
      or (a.entity_name ilike '%sigiloso%' and a.action::text in ('READ','DOWNLOAD'))
      or a.action::text = 'MFA_FAILED'
  )
order by a.event_time desc
limit 20;", new { tenantId }, cancellationToken: ct))).AsList();

        return new AuditDashboardVM
        {
            TotalEventsLast24h = (int)row.total_events,
            AccessDeniedLast24h = (int)row.access_denied,
            ChangedDocumentsLast24h = (int)row.changed_documents,
            AuthenticationEventsLast24h = (int)row.auth_events,
            Alerts = alerts
        };
    }
}
