namespace InovaGed.Application.Audit;

public sealed class SystemLogFilter
{
    public Guid TenantId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? EventType { get; set; }
    public string? Action { get; set; }
    public Guid? UserId { get; set; }
    public string? UserText { get; set; }
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? Path { get; set; }
    public int? HttpStatus { get; set; }
    public string? Search { get; set; }
    public string? CorrelationId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class SystemLogListItemDto
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string EventType { get; set; } = "-";
    public string Action { get; set; } = "-";
    public string? UserName { get; set; }
    public string? Path { get; set; }
    public int? HttpStatus { get; set; }
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Source { get; set; } = "-";
    public string? CorrelationId { get; set; }
}

public sealed class SystemLogDetailsDto
{
    public string Id { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
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
    public string? DataJson { get; set; }
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long Total { get; init; }
}
