
namespace InovaGed.Infrastructure.Ged.Loans
{
    public sealed record AuditWriteEntry(
      Guid TenantId,
      Guid? UserId,
      string Action,              // ex: CREATE/UPDATE/DELETE/ACCESS_DENIED/HTTP
      string EntityName,          // ex: LOAN_REQUEST / BATCH / BOX / DOCUMENT
      Guid? EntityId,
      string? Summary,
      string? Ip,
      string? UserAgent,
      string EventType,           // INFO/WARN/ERROR/SECURITY (mapeia para audit_event_type)
      bool IsSuccess,
      int? HttpStatus,
      string? CorrelationId,
      object? Data);
}