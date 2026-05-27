namespace InovaGed.Application.Audit;

public interface IAppAuditLogService
{
    Task LogAsync(AppAuditLogEntry entry, CancellationToken ct = default);
    Task LogErrorAsync(Guid? tenantId, Guid? userId, string source, string message, Exception? exception, object? data = null, CancellationToken ct = default);
    Task LogSecurityAsync(Guid? tenantId, Guid? userId, string action, string message, object? data = null, CancellationToken ct = default);
    Task LogBusinessAsync(Guid? tenantId, Guid? userId, string action, string entityName, Guid? entityId, string message, object? data = null, string? entityKey = null, CancellationToken ct = default);
    Task LogHttpAsync(Guid? tenantId, Guid? userId, string method, string path, int? statusCode, long elapsedMs, object? data = null, CancellationToken ct = default);
}
