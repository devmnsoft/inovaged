using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanReportService(IDbConnectionFactory db, IAuditWriter audit) : ILoanReportService
{
    public async Task<LoanReportResult> RunAsync(Guid tenantId, Guid actorId, LoanReportFilter filter, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        const string sql = """
select id as Id, protocol_no as ProtocolNo, coalesce(requester_name,'') as RequesterName,
coalesce(requester_sector_name,requester_sector) as Sector,status::text as Status,delivery_mode as DeliveryMode,
requested_at as RequestedAt,due_at as DueAt,returned_at as ReturnedAt,
greatest(0,extract(day from (coalesce(returned_at,now())-due_at)))::int as DaysLate,coalesce(collection_count,0) as CollectionCount
from ged.loan_request where tenant_id=@tenantId and coalesce(reg_status,'A')='A'
and (@from is null or requested_at>=@from) and (@to is null or requested_at<@to + interval '1 day')
and (@status is null or status::text=@status) and (@requester is null or requester_name ilike '%'||@requester||'%')
and (@sector is null or coalesce(requester_sector_name,requester_sector,'') ilike '%'||@sector||'%')
and (@mode is null or delivery_mode=@mode) and (@overdue is not true or due_at<now() and returned_at is null)
order by requested_at desc limit 5000;
""";
        var rows = (await conn.QueryAsync<LoanReportRow>(new CommandDefinition(sql, new { tenantId, from=filter.From, to=filter.To, status=Null(filter.Status), requester=Null(filter.Requester), sector=Null(filter.Sector), mode=Null(filter.DeliveryMode), overdue=filter.OverdueOnly }, cancellationToken: ct))).AsList();
        await conn.ExecuteAsync(new CommandDefinition("insert into ged.loan_report_run(tenant_id,run_by,filters_json,row_count) values(@tenantId,@actorId,@filters::jsonb,@count)", new { tenantId, actorId, filters=System.Text.Json.JsonSerializer.Serialize(filter), count=rows.Count }, cancellationToken: ct));
        await audit.WriteAsync(tenantId, actorId, "LOAN_REPORT_RUN", "loan_report_run", null, "Relatório operacional executado", null, null, new { rows=rows.Count }, ct);
        return new LoanReportResult { Rows=rows };
    }
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
