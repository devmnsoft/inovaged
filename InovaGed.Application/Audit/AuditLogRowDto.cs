namespace InovaGed.Application.Audit
{
    public sealed record AuditLogRowDto(
     long Id,
     DateTime EventTime,
     string EventType,
     Guid? UserId,
     string? UserName,
     string? UserEmail,
     string Action,
     string EntityName,
     Guid? EntityId,
     string? Summary,
     string? IpAddress
 );
}
