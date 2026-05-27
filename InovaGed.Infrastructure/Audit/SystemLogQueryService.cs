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
        f.Page = Math.Max(1, f.Page); f.PageSize = Math.Clamp(f.PageSize, 1, 200);
        using var c = await _db.OpenAsync(ct);
        var w = " where tenant_id=@tenantId and (@eventType is null or event_type=@eventType) and (@action is null or action::text=@action) and (@path is null or path ilike @pathLike) and (@httpStatus is null or http_status=@httpStatus) and (@corr is null or correlation_id=@corr) and (@search is null or summary ilike @searchLike) ";
        var p = new { tenantId = f.TenantId, eventType = f.EventType, action = f.Action, path = f.Path, pathLike = $"%{f.Path}%", httpStatus = f.HttpStatus, corr = f.CorrelationId, search = f.Search, searchLike = $"%{f.Search}%", lim = f.PageSize, off = (f.Page - 1) * f.PageSize };
        var items = (await c.QueryAsync<SystemLogListItemDto>($@"select id::text as Id,event_time as CreatedAt,coalesce(event_type,'INFO') as EventType,coalesce(action::text,'-') as Action,user_name as UserName,path,http_status as HttpStatus,entity_name as EntityName,entity_id as EntityId,coalesce(summary,'-') as Summary,coalesce(source,'-') as Source,correlation_id as CorrelationId from ged.audit_log {w} order by event_time desc limit @lim offset @off", p)).ToList();
        var total = await c.ExecuteScalarAsync<long>($"select count(1) from ged.audit_log {w}", p);
        return new PagedResult<SystemLogListItemDto> { Items = items, Page = f.Page, PageSize = f.PageSize, Total = total };
    }

    public async Task<SystemLogDetailsDto?> GetDetailsAsync(string id, Guid tenantId, CancellationToken ct)
    {
        using var c = await _db.OpenAsync(ct);
        return await c.QueryFirstOrDefaultAsync<SystemLogDetailsDto>("select id::text as Id, tenant_id as TenantId, user_id as UserId, user_name as UserName, event_time as CreatedAt, coalesce(event_type, 'INFO') as EventType, coalesce(action::text, '-') as Action, coalesce(source, '-') as Source, entity_name as EntityName, entity_id as EntityId, coalesce(summary, '-') as Summary, details, exception_type as ExceptionType, exception_message as ExceptionMessage, stack_trace as StackTrace, path, http_method as HttpMethod, http_status as HttpStatus, ip_address as IpAddress, user_agent as UserAgent, elapsed_ms as ElapsedMs, correlation_id as CorrelationId, data::text as DataJson from ged.audit_log where tenant_id=@tenantId and id::text=@id", new { tenantId, id });
    }

    public async Task<byte[]> ExportCsvAsync(SystemLogFilter filter, CancellationToken ct)
    {
        filter.Page = 1; filter.PageSize = 200;
        var rows = await SearchAsync(filter, ct);
        var sb = new StringBuilder("Data,Tipo,Acao,Usuario,Rota,Status,Resumo,Source,CorrelationId\n");
        foreach (var x in rows.Items) sb.AppendLine($"\"{x.CreatedAt:O}\",\"{x.EventType}\",\"{x.Action}\",\"{x.UserName}\",\"{x.Path}\",\"{x.HttpStatus}\",\"{x.Summary?.Replace("\"", "'")}\",\"{x.Source}\",\"{x.CorrelationId}\"");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
