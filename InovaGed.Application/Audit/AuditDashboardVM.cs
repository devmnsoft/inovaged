namespace InovaGed.Application.Audit;

public sealed class AuditDashboardVM
{
    public int TotalEventsLast24h { get; init; }
    public int AccessDeniedLast24h { get; init; }
    public int ChangedDocumentsLast24h { get; init; }
    public int AuthenticationEventsLast24h { get; init; }
    public IReadOnlyList<SuspiciousActivityAlertDto> Alerts { get; init; } = Array.Empty<SuspiciousActivityAlertDto>();
}

public sealed record SuspiciousActivityAlertDto(
    DateTime EventTime,
    Guid? UserId,
    string? UserName,
    string Action,
    string EntityName,
    string? Summary,
    string? IpAddress);
