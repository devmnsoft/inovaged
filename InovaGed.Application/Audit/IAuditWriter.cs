using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InovaGed.Application.Audit
{
    public interface IAuditWriter
    {
        Task WriteAsync(Guid tenantId, Guid? userId, string? userDisplay,
            string action, string? entityType, Guid? entityId,
            bool success, object? details, string? ip, string? userAgent,
            CancellationToken ct);
    }

}
