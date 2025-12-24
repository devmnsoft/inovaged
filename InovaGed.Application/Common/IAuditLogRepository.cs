using InovaGed.Domain.Auditing;

namespace InovaGed.Application.Common;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct);
}
