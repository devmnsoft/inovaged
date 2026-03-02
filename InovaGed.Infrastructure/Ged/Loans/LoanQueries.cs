using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanQueries : ILoanQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<LoanQueries> _logger;

    public LoanQueries(IDbConnectionFactory db, ILogger<LoanQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LoanRowDto>> ListAsync(Guid tenantId, string? q, string? status, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            q ??= "";
            var qTrim = q.Trim();

            long? protocolNo = null;
            if (!string.IsNullOrWhiteSpace(qTrim) && long.TryParse(qTrim, out var p))
                protocolNo = p;

            const string sql = """
select
    l.id,
    l.protocol_no                 as ProtocolNo,
    l.status::text                as Status,
    coalesce(l.requester_name,'') as RequesterName,
    l.requested_at                as RequestedAt,
    l.due_at                      as DueAt,
    l.approved_at                 as ApprovedAt,
    l.delivered_at                as DeliveredAt,
    l.returned_at                 as ReturnedAt,
    (
        select count(*)::int
        from ged.loan_request_item i
        where i.tenant_id = l.tenant_id
          and i.loan_id   = l.id
          and i.reg_status='A'
    )                             as ItemsCount
from ged.loan_request l
where l.tenant_id = @TenantId
  and l.reg_status = 'A'
  and (@Status is null or @Status = '' or l.status::text = @Status)
  and (
        @Q is null or @Q = ''
        or coalesce(l.requester_name,'') ilike ('%' || @Q || '%')
        or l.status::text ilike ('%' || @Q || '%')
        or l.protocol_no::text ilike ('%' || @Q || '%')
        or (@ProtocolNo is not null and l.protocol_no = @ProtocolNo)
  )
order by l.requested_at desc
limit 200;
""";

            var rows = await con.QueryAsync<LoanRowDto>(new CommandDefinition(
                sql,
                new { TenantId = tenantId, Q = qTrim, Status = status, ProtocolNo = protocolNo },
                cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanQueries.ListAsync failed. Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<IReadOnlyList<LoanRowDto>> ListOverdueAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            const string sql = """
select
    l.id,
    l.protocol_no                 as ProtocolNo,
    l.status::text                as Status,
    coalesce(l.requester_name,'') as RequesterName,
    l.requested_at                as RequestedAt,
    l.due_at                      as DueAt,
    l.approved_at                 as ApprovedAt,
    l.delivered_at                as DeliveredAt,
    l.returned_at                 as ReturnedAt,
    (
        select count(*)::int
        from ged.loan_request_item i
        where i.tenant_id = l.tenant_id
          and i.loan_id   = l.id
          and i.reg_status='A'
    )                             as ItemsCount
from ged.loan_request l
where l.tenant_id = @TenantId
  and l.reg_status='A'
  and l.due_at < now()
  and l.returned_at is null
order by l.due_at asc
limit 200;
""";

            var rows = await con.QueryAsync<LoanRowDto>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanQueries.ListOverdueAsync failed. Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<LoanDetailsVM?> GetAsync(Guid tenantId, Guid loanId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string headSql = @"
select
  lr.id,
  lr.protocol_no as ProtocolNo,
  lr.status::text as Status,
  lr.requester_name as RequesterName,
  lr.requested_at as RequestedAt,
  lr.due_at as DueAt,
  lr.approved_at as ApprovedAt,
  lr.delivered_at as DeliveredAt,
  lr.returned_at as ReturnedAt,
  (select count(*)::int from ged.loan_request_item i
     where i.tenant_id = lr.tenant_id and i.loan_id = lr.id and i.reg_status='A') as ItemsCount
from ged.loan_request lr
where lr.tenant_id = @tenant_id and lr.id = @loan_id and lr.reg_status='A';
";
            var header = await conn.QuerySingleOrDefaultAsync<LoanRowDto>(
                new CommandDefinition(headSql, new { tenant_id = tenantId, loan_id = loanId }, cancellationToken: ct));

            if (header is null) return null;

            const string itemsSql = @"
select
  i.document_id as DocumentId,
  i.is_physical as IsPhysical,
  d.code as DocumentCode,
  d.title as DocumentTitle,
  dt.name as DocumentType
from ged.loan_request_item i
join ged.document d on d.tenant_id=i.tenant_id and d.id=i.document_id
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id
where i.tenant_id=@tenant_id and i.loan_id=@loan_id and i.reg_status='A'
order by d.title;
";
            var items = (await conn.QueryAsync<LoanItemDto>(
                new CommandDefinition(itemsSql, new { tenant_id = tenantId, loan_id = loanId }, cancellationToken: ct))).AsList();

            const string histSql = @"
select
  h.event_time as EventTime,
  h.event_type as EventType,
  u.name as ByUserName,
  h.notes as Notes
from ged.loan_history h
left join ged.app_user u on u.tenant_id=h.tenant_id and u.id=h.by_user_id
where h.tenant_id=@tenant_id and h.loan_id=@loan_id and h.reg_status='A'
order by h.event_time desc;
";
            var history = (await conn.QueryAsync<LoanEventDto>(
                new CommandDefinition(histSql, new { tenant_id = tenantId, loan_id = loanId }, cancellationToken: ct))).AsList();

            return new LoanDetailsVM { Header = header, Items = items, History = history };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanQueries.GetAsync failed. Tenant={Tenant} Loan={Loan}", tenantId, loanId);
            return null;
        }
    }
}