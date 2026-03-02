namespace InovaGed.Application.Retention;

public interface IRetentionAuditWriter
{
    Task WriteAsync(Guid tenantId, Guid actorId, object entityId, string eventType, string? message, CancellationToken ct);
    Task WriteDocAsync(Guid tenantId, Guid actorId, string? actorEmail, Guid documentId, string eventType, object data, CancellationToken ct);
}