namespace InovaGed.Application.Audit;

public sealed class AppAuditLogEntry
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string EventType { get; set; } = "AUDIT";
    public string Action { get; set; } = "VIEW";
    public string Source { get; set; } = "APP";
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? Path { get; set; }
    public string? HttpMethod { get; set; }
    public int? HttpStatus { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public long? ElapsedMs { get; set; }
    public string? CorrelationId { get; set; }
    public object? Data { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
