using System.Data;
using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Audit;

public sealed class SystemLogQueryService : ISystemLogQueryService
{
    private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INFO",
        "SECURITY",
        "ACCESS_DENIED",
        "ERROR",
        "AUDIT",
        "BUSINESS",
        "SYSTEM"
    };

    private readonly IDbConnectionFactory _db;
    public SystemLogQueryService(IDbConnectionFactory db) => _db = db;

    public async Task<PagedResult<SystemLogListItemDto>> SearchAsync(SystemLogFilter f, CancellationToken ct)
    {
        f.Page = Math.Max(1, f.Page);
        f.PageSize = Math.Clamp(f.PageSize, 1, 200);

        using var c = await _db.OpenAsync(ct);
        var auditColumns = await GetColumnsAsync(c, "ged", "audit_log", ct);
        var userColumns = await GetColumnsAsync(c, "ged", "app_user", ct);
        var query = BuildQuery(auditColumns, userColumns, includeDetails: false);

        var pageSize = f.PageSize;
        var offset = (f.Page - 1) * f.PageSize;
        var p = CreateParameters(f, pageSize, offset);

        var items = (await c.QueryAsync<SystemLogListItemDto>(new CommandDefinition(query.ListSql, p, cancellationToken: ct))).ToList();
        var total = await c.ExecuteScalarAsync<long>(new CommandDefinition(query.CountSql, p, cancellationToken: ct));
        return new PagedResult<SystemLogListItemDto> { Items = items, Page = f.Page, PageSize = f.PageSize, Total = total };
    }

    public async Task<SystemLogDetailsDto?> GetDetailsAsync(string id, Guid tenantId, CancellationToken ct)
    {
        using var c = await _db.OpenAsync(ct);
        var auditColumns = await GetColumnsAsync(c, "ged", "audit_log", ct);
        var userColumns = await GetColumnsAsync(c, "ged", "app_user", ct);
        var query = BuildQuery(auditColumns, userColumns, includeDetails: true);

        var p = new DynamicParameters();
        p.Add("TenantId", tenantId, DbType.Guid);
        p.Add("Id", NullIfWhiteSpace(id), DbType.String);

        return await c.QueryFirstOrDefaultAsync<SystemLogDetailsDto>(new CommandDefinition(query.DetailsSql, p, cancellationToken: ct));
    }

    public async Task<byte[]> ExportCsvAsync(SystemLogFilter filter, CancellationToken ct)
    {
        filter.Page = 1;
        filter.PageSize = 200;
        var rows = await SearchAsync(filter, ct);
        var sb = new StringBuilder("Data,Tipo,Acao,UsuarioId,Usuario,Rota,Metodo,Status,Resumo,Source,CorrelationId,TenantId\n");
        foreach (var x in rows.Items)
        {
            sb.AppendLine($"\"{x.CreatedAt:O}\",\"{EscapeCsv(x.EventType)}\",\"{EscapeCsv(x.Action)}\",\"{x.UserId}\",\"{EscapeCsv(x.UserName)}\",\"{EscapeCsv(x.Path)}\",\"{EscapeCsv(x.HttpMethod)}\",\"{x.HttpStatus}\",\"{EscapeCsv(x.Summary)}\",\"{EscapeCsv(x.Source)}\",\"{EscapeCsv(x.CorrelationId)}\",\"{x.TenantId}\"");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static DynamicParameters CreateParameters(SystemLogFilter f, int pageSize, int offset)
    {
        var p = new DynamicParameters();
        p.Add("TenantId", f.TenantId, DbType.Guid);
        p.Add("From", f.From, DbType.DateTimeOffset);
        p.Add("To", f.To, DbType.DateTimeOffset);
        p.Add("Action", NullIfWhiteSpace(f.Action), DbType.String);
        p.Add("EventType", NormalizeEventTypeFilter(f.EventType), DbType.String);
        p.Add("Source", NullIfWhiteSpace(f.Source), DbType.String);
        p.Add("UserId", f.UserId, DbType.Guid);
        p.Add("User", NullIfWhiteSpace(f.UserText), DbType.String);
        p.Add("EntityName", NullIfWhiteSpace(f.EntityName), DbType.String);
        p.Add("EntityId", NullIfWhiteSpace(f.EntityId), DbType.String);
        p.Add("Path", NullIfWhiteSpace(f.Path), DbType.String);
        p.Add("HttpStatus", f.HttpStatus, DbType.Int32);
        p.Add("CorrelationId", NullIfWhiteSpace(f.CorrelationId), DbType.String);
        p.Add("Search", NullIfWhiteSpace(f.Search), DbType.String);
        p.Add("PageSize", pageSize, DbType.Int32);
        p.Add("Offset", offset, DbType.Int32);
        return p;
    }

    private static BuiltQuery BuildQuery(IReadOnlySet<string> auditColumns, IReadOnlySet<string> userColumns, bool includeDetails)
    {
        var dateExpr = FirstExisting(auditColumns, "l", "created_at", "created_at_utc", "reg_date", "event_time", "occurred_at", "happened_at") ?? "now()";
        var orderExpr = dateExpr == "now()" && auditColumns.Contains("id") ? "l.id" : dateExpr;
        var eventExpr = TextExpr(auditColumns, "l", "event_type", "type", "level") ?? "'-'::text";
        var actionExpr = TextExpr(auditColumns, "l", "action", "audit_action") ?? "'-'::text";
        var sourceExpr = TextExpr(auditColumns, "l", "source", "module", "entity") ?? "'-'::text";
        var userIdExpr = TextExpr(auditColumns, "l", "user_id", "actor_id", "created_by") ?? "null::text";
        var pathExpr = TextExpr(auditColumns, "l", "path", "request_path", "url") ?? "null::text";
        var methodExpr = TextExpr(auditColumns, "l", "http_method", "method") ?? "null::text";
        var statusExpr = IntExpr(auditColumns, "l", "http_status", "status_code") ?? "null::integer";
        var entityNameExpr = TextExpr(auditColumns, "l", "entity_name", "entity_type") ?? "null::text";
        var entityIdExpr = TextExpr(auditColumns, "l", "entity_id") ?? "null::text";
        var messageExpr = TextExpr(auditColumns, "l", "message", "summary") ?? "''::text";
        var detailsExpr = TextExpr(auditColumns, "l", "details", "details_json", "data", "payload", "metadata") ?? "null::text";
        var correlationExpr = TextExpr(auditColumns, "l", "correlation_id", "correlationid") ?? "null::text";
        var tenantExpr = auditColumns.Contains("tenant_id") ? "l.tenant_id" : "null::uuid";
        var idExpr = TextExpr(auditColumns, "l", "id") ?? "''::text";

        var join = BuildUserJoin(auditColumns, userColumns);
        var userNameExpr = BuildUserNameExpression(auditColumns, userColumns, join.Length > 0);
        var userFilters = BuildUserFilters(auditColumns, userColumns, join.Length > 0);

        var where = new List<string>();
        if (auditColumns.Contains("tenant_id")) where.Add("l.tenant_id = (@TenantId)::uuid");
        where.Add($"((@From)::timestamptz is null or {dateExpr} >= (@From)::timestamptz)");
        where.Add($"((@To)::timestamptz is null or {dateExpr} <= (@To)::timestamptz)");
        where.Add($"((@Action)::text is null or {actionExpr} = (@Action)::text)");
        where.Add($"((@EventType)::text is null or {eventExpr} = (@EventType)::text)");
        where.Add($"((@Source)::text is null or {sourceExpr} ilike '%' || (@Source)::text || '%')");
        where.Add($"((@UserId)::uuid is null or {userIdExpr} = (@UserId)::text)");
        where.Add(userFilters);
        where.Add($"((@EntityName)::text is null or {entityNameExpr} ilike '%' || (@EntityName)::text || '%')");
        where.Add($"((@EntityId)::text is null or {entityIdExpr} = (@EntityId)::text)");
        where.Add($"((@Path)::text is null or {pathExpr} ilike '%' || (@Path)::text || '%')");
        where.Add($"((@HttpStatus)::int is null or {statusExpr} = (@HttpStatus)::int)");
        where.Add($"((@CorrelationId)::text is null or {correlationExpr} = (@CorrelationId)::text)");
        where.Add($"((@Search)::text is null or {messageExpr} ilike '%' || (@Search)::text || '%' or {detailsExpr} ilike '%' || (@Search)::text || '%')");
        var whereSql = "where " + string.Join("\nand ", where);

        var fromSql = $"from ged.audit_log l\n{join}";
        var selectCommon = $@"{idExpr} as ""Id"",
{tenantExpr} as ""TenantId"",
{dateExpr} as ""CreatedAt"",
coalesce({eventExpr}, '-') as ""EventType"",
coalesce({actionExpr}, '-') as ""Action"",
{userIdExpr} as ""UserId"",
{userNameExpr} as ""UserName"",
{methodExpr} as ""HttpMethod"",
{pathExpr} as ""Path"",
{statusExpr} as ""HttpStatus"",
{entityNameExpr} as ""EntityName"",
{entityIdExpr} as ""EntityId"",
coalesce({messageExpr}, '-') as ""Summary"",
{detailsExpr} as ""Details"",
coalesce({sourceExpr}, '-') as ""Source"",
{correlationExpr} as ""CorrelationId""";

        var listSql = $@"select {selectCommon}
{fromSql}
{whereSql}
order by {orderExpr} desc
limit (@PageSize)::int offset (@Offset)::int";

        var countSql = $@"select count(1)
{fromSql}
{whereSql}";

        var detailsExtra = includeDetails ? $@",
{TextExpr(auditColumns, "l", "exception_type") ?? "null::text"} as ""ExceptionType"",
{TextExpr(auditColumns, "l", "exception_message") ?? "null::text"} as ""ExceptionMessage"",
{TextExpr(auditColumns, "l", "stack_trace") ?? "null::text"} as ""StackTrace"",
{TextExpr(auditColumns, "l", "ip_address") ?? "null::text"} as ""IpAddress"",
{TextExpr(auditColumns, "l", "user_agent") ?? "null::text"} as ""UserAgent"",
{BigIntExpr(auditColumns, "l", "elapsed_ms") ?? "null::bigint"} as ""ElapsedMs"",
{TextExpr(auditColumns, "l", "data", "details_json") ?? "null::text"} as ""DataJson""" : string.Empty;

        var detailsSql = $@"select {selectCommon}{detailsExtra}
{fromSql}
where {(auditColumns.Contains("tenant_id") ? "l.tenant_id = (@TenantId)::uuid and " : string.Empty)}{idExpr} = (@Id)::text";

        return new BuiltQuery(listSql, countSql, detailsSql);
    }

    private static string BuildUserJoin(IReadOnlySet<string> auditColumns, IReadOnlySet<string> userColumns)
    {
        if (userColumns.Count == 0 || !auditColumns.Contains("user_id") || !userColumns.Contains("id"))
            return string.Empty;

        var tenantJoin = auditColumns.Contains("tenant_id") && userColumns.Contains("tenant_id")
            ? " and u.tenant_id = l.tenant_id"
            : string.Empty;

        return $"left join ged.app_user u on u.id = l.user_id{tenantJoin}";
    }

    private static string BuildUserNameExpression(IReadOnlySet<string> auditColumns, IReadOnlySet<string> userColumns, bool hasJoin)
    {
        var parts = new List<string>();
        if (hasJoin)
        {
            foreach (var column in new[] { "name", "full_name", "email", "login", "user_name" })
            {
                if (userColumns.Contains(column)) parts.Add($"nullif(u.{column}::text, '')");
            }
        }

        if (auditColumns.Contains("user_id")) parts.Add("l.user_id::text");
        parts.Add("'Sistema'");
        return $"coalesce({string.Join(", ", parts)})";
    }

    private static string BuildUserFilters(IReadOnlySet<string> auditColumns, IReadOnlySet<string> userColumns, bool hasJoin)
    {
        var parts = new List<string>();
        if (auditColumns.Contains("user_id")) parts.Add("l.user_id::text ilike '%' || (@User)::text || '%'");
        if (hasJoin)
        {
            foreach (var column in new[] { "name", "full_name", "email", "login", "user_name" })
            {
                if (userColumns.Contains(column)) parts.Add($"u.{column}::text ilike '%' || (@User)::text || '%'");
            }
        }

        return parts.Count == 0
            ? "(@User)::text is null"
            : $"((@User)::text is null or {string.Join(" or ", parts)})";
    }

    private static string? FirstExisting(IReadOnlySet<string> columns, string alias, params string[] names)
        => names.FirstOrDefault(columns.Contains) is { } column ? $"{alias}.{column}" : null;

    private static string? TextExpr(IReadOnlySet<string> columns, string alias, params string[] names)
        => FirstExisting(columns, alias, names) is { } expr ? $"{expr}::text" : null;

    private static string? IntExpr(IReadOnlySet<string> columns, string alias, params string[] names)
        => FirstExisting(columns, alias, names) is { } expr ? $"{expr}::integer" : null;

    private static string? BigIntExpr(IReadOnlySet<string> columns, string alias, params string[] names)
        => FirstExisting(columns, alias, names) is { } expr ? $"{expr}::bigint" : null;

    private static async Task<IReadOnlySet<string>> GetColumnsAsync(IDbConnection conn, string schemaName, string tableName, CancellationToken ct)
    {
        const string sql = @"select column_name
from information_schema.columns
where table_schema = @SchemaName
  and table_name = @TableName;";

        var columns = await conn.QueryAsync<string>(new CommandDefinition(sql, new { SchemaName = schemaName, TableName = tableName }, cancellationToken: ct));
        return columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeEventTypeFilter(string? eventType)
    {
        var value = eventType?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return AllowedEventTypes.Contains(value) ? value.ToUpperInvariant() : null;
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeCsv(string? value) => (value ?? string.Empty).Replace("\"", "'");

    private sealed record BuiltQuery(string ListSql, string CountSql, string DetailsSql);
}
