using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Audit;

public sealed class AuditQueries : IAuditQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<AuditQueries> _logger;

    public AuditQueries(IDbConnectionFactory db, ILogger<AuditQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<AuditIndexVM> ListAsync(Guid tenantId, AuditSearchVM search, CancellationToken ct)
        => LoadAsync(tenantId, search, accessDeniedOnly: false, ct);

    public Task<AuditIndexVM> ListAccessDeniedAsync(Guid tenantId, AuditSearchVM search, CancellationToken ct)
        => LoadAsync(tenantId, search, accessDeniedOnly: true, ct);

    private async Task<AuditIndexVM> LoadAsync(Guid tenantId, AuditSearchVM search, bool accessDeniedOnly, CancellationToken ct)
    {
        try
        {
            // defaults: últimos 7 dias
            var now = DateTime.UtcNow;
            var from = (search.From ?? now.AddDays(-7)).Date;
            var to = (search.To ?? now).Date.AddDays(1).AddTicks(-1);

            var q = (search.Q ?? "").Trim();
            var page = search.Page <= 0 ? 1 : search.Page;
            var pageSize = search.PageSize <= 0 ? 50 : Math.Min(search.PageSize, 200);
            var offset = (page - 1) * pageSize;

            var eventType = string.IsNullOrWhiteSpace(search.EventType) ? null : search.EventType!.Trim();
            var action = string.IsNullOrWhiteSpace(search.Action) ? null : search.Action!.Trim();

            await using var conn = await _db.OpenAsync(ct);

            var where = @"
where a.tenant_id = @tenant_id
  and a.event_time >= @from
  and a.event_time <= @to
";

            if (accessDeniedOnly)
            {
                where += @"
  and (a.action::text = 'ACCESS_DENIED' or a.event_type::text = 'ACCESS_DENIED')
";
            }

            where += @"
  and (@eventType is null or a.event_type::text = @eventType)
  and (@action is null or a.action::text = @action)
  and (
        @q = ''
        or coalesce(u.name,'') ilike ('%'||@q||'%')
        or coalesce(u.email,'') ilike ('%'||@q||'%')
        or coalesce(a.entity_name,'') ilike ('%'||@q||'%')
        or coalesce(a.summary,'') ilike ('%'||@q||'%')
        or coalesce(a.ip_address,'') ilike ('%'||@q||'%')
      )
";

            var countSql = $@"
select count(*)::int
from ged.audit_log a
left join ged.users u on u.tenant_id=a.tenant_id and u.id=a.user_id
{where};
";

            var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                countSql,
                new { tenant_id = tenantId, from, to, q, eventType, action },
                cancellationToken: ct));

            var listSql = $@"
select
  a.id as ""Id"",
  a.event_time as ""EventTime"",
  a.event_type::text as ""EventType"",
  a.user_id as ""UserId"",
  u.name as ""UserName"",
  u.email as ""UserEmail"",
  a.action::text as ""Action"",
  a.entity_name as ""EntityName"",
  a.entity_id as ""EntityId"",
  a.summary as ""Summary"",
  a.ip_address as ""IpAddress""
from ged.audit_log a
left join ged.users u on u.tenant_id=a.tenant_id and u.id=a.user_id
{where}
order by a.event_time desc
limit @limit offset @offset;
";

            var rows = (await conn.QueryAsync<AuditLogRowDto>(new CommandDefinition(
                listSql,
                new { tenant_id = tenantId, from, to, q, eventType, action, limit = pageSize, offset },
                cancellationToken: ct))).AsList();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);

            return new AuditIndexVM
            {
                Search = new AuditSearchVM
                {
                    From = from,
                    To = to.Date, // mantém só a data para o date input
                    Q = q,
                    EventType = eventType,
                    Action = action,
                    Page = page,
                    PageSize = pageSize
                },
                Rows = rows,
                Total = total,
                TotalPages = totalPages <= 0 ? 1 : totalPages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AuditQueries.LoadAsync failed Tenant={Tenant}", tenantId);
            return new AuditIndexVM();
        }
    }
}