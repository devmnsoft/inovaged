using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Audit
{
    public interface IAuditWriter
    {
        Task<Result> WriteAsync(
            Guid tenantId,
            Guid? userId,
            string action,            // usa enum no banco (audit_action_enum) como string
            string entityName,
            Guid? entityId,
            string? summary,
            string? ipAddress,
            string? userAgent,
            object? data,
            CancellationToken ct);
    }

}
