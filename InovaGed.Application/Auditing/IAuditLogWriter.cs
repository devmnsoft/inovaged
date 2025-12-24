using System.Data;

namespace InovaGed.Application.Auditing
{
    public interface IAuditLogWriter
    {
        Task InsertAsync(AuditLogRow row, IDbTransaction tx, CancellationToken ct);
    }
}
