using System.Text.Json;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Audit;

public sealed class AppAuditLogService : IAppAuditLogService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<AppAuditLogService> _logger;

    public AppAuditLogService(IDbConnectionFactory db, ILogger<AppAuditLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task LogErrorAsync(Guid? tenantId, Guid? userId, string source, string message, Exception? exception, object? data = null, CancellationToken ct = default)
        => LogAsync(new AppAuditLogEntry { TenantId = tenantId, UserId = userId, Source = source, EventType = "ERROR", Action = "HTTP", Summary = message, ExceptionType = exception?.GetType().Name, ExceptionMessage = exception?.Message, StackTrace = exception?.StackTrace, Data = data }, ct);
    public Task LogSecurityAsync(Guid? tenantId, Guid? userId, string action, string message, object? data = null, CancellationToken ct = default)
        => LogAsync(new AppAuditLogEntry { TenantId = tenantId, UserId = userId, Source = "SECURITY", EventType = "SECURITY", Action = NormalizeAction(action), Summary = message, Data = data }, ct);
    public Task LogBusinessAsync(Guid? tenantId, Guid? userId, string action, string entityName, string? entityId, string message, object? data = null, CancellationToken ct = default)
        => LogAsync(new AppAuditLogEntry { TenantId = tenantId, UserId = userId, Source = "BUSINESS", EventType = "BUSINESS", Action = NormalizeAction(action), EntityName = entityName, EntityId = entityId, Summary = message, Data = data }, ct);
    public Task LogHttpAsync(Guid? tenantId, Guid? userId, string method, string path, int? statusCode, long elapsedMs, object? data = null, CancellationToken ct = default)
        => LogAsync(new AppAuditLogEntry { TenantId = tenantId, UserId = userId, Source = "HTTP", EventType = "AUDIT", Action = "HTTP", Path = path, HttpMethod = method, HttpStatus = statusCode, ElapsedMs = elapsedMs, Summary = $"{method} {path} => {statusCode}", Data = data }, ct);

    public async Task LogAsync(AppAuditLogEntry e, CancellationToken ct = default)
    {
        try
        {
            if (e.TenantId is null || e.TenantId == Guid.Empty) return;
            using var conn = await _db.OpenAsync(ct);
            const string sql = @"insert into ged.audit_log
(tenant_id,user_id,action,entity_name,entity_id,summary,ip_address,user_agent,data,event_time,event_type,source,details,exception_type,exception_message,stack_trace,path,http_method,http_status,elapsed_ms,correlation_id)
values
(@TenantId,@UserId,@Action::ged.audit_action_enum,@EntityName,@EntityId,@Summary,@IpAddress,@UserAgent,@Data::jsonb,@CreatedAt,@EventType,@Source,@Details,@ExceptionType,@ExceptionMessage,@StackTrace,@Path,@HttpMethod,@HttpStatus,@ElapsedMs,@CorrelationId);";
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                e.TenantId,e.UserId,Action = NormalizeAction(e.Action),e.EntityName,e.EntityId,e.Summary,e.IpAddress,e.UserAgent,
                Data = e.Data is null ? null : JsonSerializer.Serialize(e.Data), CreatedAt = e.CreatedAt, e.EventType,e.Source,e.Details,e.ExceptionType,e.ExceptionMessage,e.StackTrace,e.Path,e.HttpMethod,e.HttpStatus,e.ElapsedMs,e.CorrelationId
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar audit log");
        }
    }

    private static string NormalizeAction(string? action)
    {
        var a = action?.Trim().ToUpperInvariant();
        return a is "CREATE" or "UPDATE" or "DELETE" or "VERSION_CREATE" or "FILE_DOWNLOAD" or "FILE_PREVIEW" or "PERMISSION_CHANGE" or "LOGIN" or "LOGOUT" or "UPLOAD" or "ADD_VERSION" or "ACCESS_DENIED" or "REPORT_PRINT" or "LOAN_EVENT" or "BATCH_EVENT" or "RETENTION_QUEUE_GENERATE" or "HTTP" or "UNLOCK_USER" or "VIEW" or "MOVE_DOCUMENT_FOLDER" or "MOVE_DOCUMENT_FOLDER_BULK" or "ACCESS_DENIED_MOVE_DOCUMENT" or "VIEW_GED_DASHBOARD" ? a : "VIEW";
    }
}
