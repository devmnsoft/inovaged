namespace InovaGed.Application.Audit;

public sealed class AuditDashboardVM
{
    public int TotalEventsLast24h { get; init; }
    public int AccessDeniedLast24h { get; init; }
    public int ChangedDocumentsLast24h { get; init; }
    public int AuthenticationEventsLast24h { get; init; }
    public IReadOnlyList<SuspiciousActivityAlertDto> Alerts { get; init; } = Array.Empty<SuspiciousActivityAlertDto>();
}

public sealed class SuspiciousActivityAlertDto
{
    public SuspiciousActivityAlertDto() { }
    public SuspiciousActivityAlertDto(DateTime EventTime, Guid? UserId, string? UserName, string Action, string EntityName, string? Summary, string? IpAddress)
    {
        this.EventTime = EventTime;
        this.UserId = UserId;
        this.UserName = UserName;
        this.Action = Action;
        this.EntityName = EntityName;
        this.Summary = Summary;
        this.IpAddress = IpAddress;
    }
    public DateTime EventTime { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string? Summary { get; set; }
    public string? IpAddress { get; set; }
}
