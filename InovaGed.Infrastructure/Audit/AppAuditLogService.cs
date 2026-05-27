using System.Text.Json;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Audit;

public sealed class AppAuditLogService : IAppAuditLogService
{
    private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INFO",
        "SECURITY",
        "ACCESS_DENIED",
        "ERROR"
    };
    private static readonly HashSet<string> AllowedActions =
    [
        "CREATE",
        "UPDATE",
        "DELETE",
        "VERSION_CREATE",
        "FILE_DOWNLOAD",
        "FILE_PREVIEW",
        "PERMISSION_CHANGE",
        "LOGIN",
        "LOGOUT",
        "UPLOAD",
        "ADD_VERSION",
        "ACCESS_DENIED",
        "REPORT_PRINT",
        "LOAN_EVENT",
        "BATCH_EVENT",
        "RETENTION_QUEUE_GENERATE",
        "HTTP",
        "UNLOCK_USER",
        "VIEW",
        "MOVE_DOCUMENT_FOLDER",
        "MOVE_DOCUMENT_FOLDER_BULK",
        "ACCESS_DENIED_MOVE_DOCUMENT",
        "VIEW_GED_DASHBOARD"
    ];

    private readonly IDbConnectionFactory _db;
    private readonly ILogger<AppAuditLogService> _logger;
    private int _schemaWarningLogged;

    public AppAuditLogService(IDbConnectionFactory db, ILogger<AppAuditLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task LogErrorAsync(Guid? tenantId, Guid? userId, string source, string message, Exception? exception, object? data = null, CancellationToken ct = default)
        => LogAsync(new AppAuditLogEntry { TenantId = tenantId, UserId = userId, Source = source, EventType = "ERROR", Action = "HTTP", Summary = message, ExceptionType = exception?.GetType().Name, ExceptionMessage = exception?.Message, StackTrace = exception?.StackTrace, Data = data }, ct);
    public Task LogSecurityAsync(Guid? tenantId, Guid? userId, string action, string message, object? data = null, CancellationToken ct = default)
        => LogAsync(new AppAuditLogEntry { TenantId = tenantId, UserId = userId, Source = "SECURITY", EventType = "SECURITY", Action = NormalizeAuditAction(action), Summary = message, Data = data }, ct);
    public Task LogBusinessAsync(Guid? tenantId, Guid? userId, string action, string entityName, Guid? entityId, string message, object? data = null, string? entityKey = null, CancellationToken ct = default)
        => LogAsync(new AppAuditLogEntry { TenantId = tenantId, UserId = userId, Source = "BUSINESS", EventType = "INFO", Action = NormalizeAuditAction(action), EntityName = entityName, EntityId = NormalizeEntityId(entityId), EntityKey = entityKey, Summary = message, Data = data }, ct);
    public Task LogHttpAsync(Guid? tenantId, Guid? userId, string method, string path, int? statusCode, long elapsedMs, object? data = null, CancellationToken ct = default)
        => LogAsync(new AppAuditLogEntry { TenantId = tenantId, UserId = userId, Source = "HTTP", EventType = "INFO", Action = "HTTP", Path = path, HttpMethod = method, HttpStatus = statusCode, ElapsedMs = elapsedMs, Summary = $"{method} {path} => {statusCode}", Data = data }, ct);

    public async Task LogAsync(AppAuditLogEntry e, CancellationToken ct = default)
    {
        try
        {
            if (e.TenantId is null || e.TenantId == Guid.Empty) return;

            using var conn = await _db.OpenAsync(ct);
            try
            {
                await conn.ExecuteAsync(new CommandDefinition(GetFullInsertSql(await ResolveDateColumnAsync(conn, ct)), BuildParams(e), cancellationToken: ct));
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
            {
                if (Interlocked.Exchange(ref _schemaWarningLogged, 1) == 0)
                {
                    _logger.LogWarning(ex,
                        "Audit log schema incompleto. Tentando insert mínimo. Tenant={TenantId} Action={Action}",
                        e.TenantId, e.Action);
                }

                await TryInsertMinimalAsync(conn, e, ct);
            }
        }
        catch (Exception ex)
        {
            var normalizedAction = NormalizeAuditAction(e.Action);
            var normalizedEventType = NormalizeEventType(e.EventType, normalizedAction);
            _logger.LogError(ex,
                "Falha ao registrar audit log. Tenant={TenantId} Action={Action} EventType={EventType} Source={Source}",
                e.TenantId, normalizedAction, normalizedEventType, e.Source);
        }
    }

    private async Task TryInsertMinimalAsync(System.Data.IDbConnection conn, AppAuditLogEntry e, CancellationToken ct)
    {
        var dateColumn = await ResolveDateColumnAsync(conn, ct);
        var sql = $@"insert into ged.audit_log
(tenant_id,user_id,action,entity_name,entity_id,summary,{dateColumn})
values
(@TenantId,@UserId,@Action::ged.audit_action_enum,@EntityName,@EntityId,@Summary,@CreatedAt);";

        await conn.ExecuteAsync(new CommandDefinition(sql, BuildParams(e), cancellationToken: ct));
    }

    private static object BuildParams(AppAuditLogEntry e) => new
    {
        Action = NormalizeAuditAction(e.Action),
        EventType = NormalizeEventType(e.EventType, e.Action),
        e.TenantId,
        e.UserId,
        e.EntityName,
        EntityId = NormalizeEntityId(e.EntityId),
        e.Summary,
        e.IpAddress,
        e.UserAgent,
        Data = BuildDataJson(e),
        CreatedAt = e.CreatedAt,
        e.Source,
        e.Details,
        e.ExceptionType,
        e.ExceptionMessage,
        e.StackTrace,
        e.Path,
        e.HttpMethod,
        e.HttpStatus,
        e.ElapsedMs,
        e.CorrelationId
    };

    private static Guid? NormalizeEntityId(Guid? entityId)
        => entityId is { } value && value != Guid.Empty ? value : null;

    private static string? BuildDataJson(AppAuditLogEntry e)
    {
        if (string.IsNullOrWhiteSpace(e.EntityKey))
        {
            return e.Data is null ? null : JsonSerializer.Serialize(e.Data);
        }

        if (e.Data is null)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?> { ["entityKey"] = e.EntityKey });
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?> { ["entityKey"] = e.EntityKey, ["payload"] = e.Data });
    }

    private static async Task<string> ResolveDateColumnAsync(System.Data.IDbConnection conn, CancellationToken ct)
    {
        const string sql = @"select column_name
from information_schema.columns
where table_schema='ged' and table_name='audit_log' and column_name in ('created_at','reg_date','event_time','occurred_at')";

        var cols = (await conn.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cols.Contains("created_at")) return "created_at";
        if (cols.Contains("reg_date")) return "reg_date";
        if (cols.Contains("event_time")) return "event_time";
        if (cols.Contains("occurred_at")) return "occurred_at";
        return "event_time";
    }

    private static string GetFullInsertSql(string dateColumn) => $@"insert into ged.audit_log
(tenant_id,user_id,action,entity_name,entity_id,summary,ip_address,user_agent,data,{dateColumn},event_type,source,details,exception_type,exception_message,stack_trace,path,http_method,http_status,elapsed_ms,correlation_id)
values
(@TenantId,@UserId,@Action::ged.audit_action_enum,@EntityName,@EntityId,@Summary,@IpAddress,@UserAgent,@Data::jsonb,@CreatedAt,@EventType::ged.audit_event_type,@Source,@Details,@ExceptionType,@ExceptionMessage,@StackTrace,@Path,@HttpMethod,@HttpStatus,@ElapsedMs,@CorrelationId);";

    private static string NormalizeAuditAction(string? action)
    {
        var value = (action ?? string.Empty).Trim().ToUpperInvariant();
        return AllowedActions.Contains(value) ? value : "HTTP";
    }

    private static string NormalizeEventType(string? eventType, string? action = null)
    {
        var value = (eventType ?? string.Empty).Trim().ToUpperInvariant();

        if (AllowedEventTypes.Contains(value))
            return value;

        var normalizedAction = NormalizeAuditAction(action);

        if (normalizedAction == "ACCESS_DENIED" || normalizedAction == "ACCESS_DENIED_MOVE_DOCUMENT")
            return "ACCESS_DENIED";

        if (normalizedAction is "LOGIN" or "LOGOUT" or "UNLOCK_USER" or "PERMISSION_CHANGE")
            return "SECURITY";

        if (value is "WARN" or "WARNING")
            return "INFO";

        return "INFO";
    }
}
