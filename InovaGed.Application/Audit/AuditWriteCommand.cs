namespace InovaGed.Application.Audit;

public sealed record AuditWriteCommand(
    Guid TenantId,
    Guid? UserId,
    string Action,
    string EntityName,
    Guid? EntityId,
    string? Summary,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    object? Data,
    string EventType = "INFO",
    string? Outcome = null,
    string? ReasonCode = null);
