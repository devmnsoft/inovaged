using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Audit;

public sealed class SystemLogQueryService : ISystemLogQueryService
{
    private readonly IDbConnectionFactory _db;
    public SystemLogQueryService(IDbConnectionFactory db) => _db = db;

    public async Task<PagedResult<SystemLogListItemDto>> SearchAsync(SystemLogFilter f, CancellationToken ct)
    {
        f.Page = Math.Max(1, f.Page);
        f.PageSize = Math.Clamp(f.PageSize, 1, 200);

        using var c = await _db.OpenAsync(ct);
        var dateColumn = await ResolveDateColumnAsync(c, ct);
        var sourceExpr = await ColumnExistsAsync(c, "source", ct) ? "coalesce(source, '-')" : "'-'::text";

        var w = " where tenant_id=@tenantId and (@eventType is null or event_type=@eventType) and (@action is null or action::text=@action) and (@path is null or path ilike @pathLike) and (@httpStatus is null or http_status=@httpStatus) and (@corr is null or correlation_id=@corr) and (@search is null or summary ilike @searchLike) ";
        var p = new { tenantId = f.TenantId, eventType = f.EventType, action = f.Action, path = f.Path, pathLike = $"%{f.Path}%", httpStatus = f.HttpStatus, corr = f.CorrelationId, search = f.Search, searchLike = $"%{f.Search}%", lim = f.PageSize, off = (f.Page - 1) * f.PageSize };

        var sql = $@"select id::text as Id,
{dateColumn} as CreatedAt,
coalesce(event_type,'INFO') as EventType,
coalesce(action::text,'-') as Action,
user_name as UserName,
path,
http_status as HttpStatus,
entity_name as EntityName,
entity_id as EntityId,
coalesce(summary,'-') as Summary,
{sourceExpr} as Source,
correlation_id as CorrelationId
from ged.audit_log {w}
order by {dateColumn} desc
limit @lim offset @off";

        var items = (await c.QueryAsync<SystemLogListItemDto>(sql, p)).ToList();
        var total = await c.ExecuteScalarAsync<long>($"select count(1) from ged.audit_log {w}", p);
        return new PagedResult<SystemLogListItemDto> { Items = items, Page = f.Page, PageSize = f.PageSize, Total = total };
    }

    public async Task<SystemLogDetailsDto?> GetDetailsAsync(string id, Guid tenantId, CancellationToken ct)
    {
        using var c = await _db.OpenAsync(ct);
        var dateColumn = await ResolveDateColumnAsync(c, ct);
        var sourceExpr = await ColumnExistsAsync(c, "source", ct) ? "coalesce(source, '-')" : "'-'::text";

        var sql = $@"select id::text as Id,
tenant_id as TenantId,
user_id as UserId,
user_name as UserName,
{dateColumn} as CreatedAt,
coalesce(event_type, 'INFO') as EventType,
coalesce(action::text, '-') as Action,
{sourceExpr} as Source,
entity_name as EntityName,
entity_id as EntityId,
coalesce(summary, '-') as Summary,
details,
exception_type as ExceptionType,
exception_message as ExceptionMessage,
stack_trace as StackTrace,
path,
http_method as HttpMethod,
http_status as HttpStatus,
ip_address as IpAddress,
user_agent as UserAgent,
elapsed_ms as ElapsedMs,
correlation_id as CorrelationId,
data::text as DataJson
from ged.audit_log
where tenant_id=@tenantId and id::text=@id";

        return await c.QueryFirstOrDefaultAsync<SystemLogDetailsDto>(sql, new { tenantId, id });
    }

    public async Task<byte[]> ExportCsvAsync(SystemLogFilter filter, CancellationToken ct)
    {
        filter.Page = 1;
        filter.PageSize = 200;
        var rows = await SearchAsync(filter, ct);
        var sb = new StringBuilder("Data,Tipo,Acao,Usuario,Rota,Status,Resumo,Source,CorrelationId\n");
        foreach (var x in rows.Items) sb.AppendLine($"\"{x.CreatedAt:O}\",\"{x.EventType}\",\"{x.Action}\",\"{x.UserName}\",\"{x.Path}\",\"{x.HttpStatus}\",\"{x.Summary?.Replace("\"", "'")}\",\"{x.Source}\",\"{x.CorrelationId}\"");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static async Task<string> ResolveDateColumnAsync(System.Data.IDbConnection conn, CancellationToken ct)
    {
        if (await ColumnExistsAsync(conn, "created_at", ct)) return "created_at";
        if (await ColumnExistsAsync(conn, "reg_date", ct)) return "reg_date";
        if (await ColumnExistsAsync(conn, "event_time", ct)) return "event_time";
        if (await ColumnExistsAsync(conn, "occurred_at", ct)) return "occurred_at";
        return "event_time";
    }

    private static async Task<bool> ColumnExistsAsync(System.Data.IDbConnection conn, string columnName, CancellationToken ct)
    {
        const string sql = @"select exists (
select 1
from information_schema.columns
where table_schema='ged' and table_name='audit_log' and column_name=@columnName
);";

        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { columnName }, cancellationToken: ct));
    }
}
