using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Audit
{
    public static class AuditWriterExtensions
    {
        /// <summary>
        /// Overload compatível com o padrão atual do projeto:
        /// (tenantId, userId, userName, action, entity, entityId, success, data, ip, ua, ct)
        /// </summary>
        public static Task<Result> WriteAsync(
            this IAuditWriter audit,
            Guid tenantId,
            Guid? userId,
            string? userName,
            string action,
            string entityName,
            Guid? entityId,
            bool success,
            object? data,
            string? ipAddress,
            string? userAgent,
            CancellationToken ct)
        {
            // summary padronizado (mantém rastreabilidade e evita ter que mexer nas chamadas)
            var summary = $"{(success ? "SUCCESS" : "FAIL")} | {action} | user={userName ?? "-"}";

            // Encaminha para o método real da interface (10 params)
            return audit.WriteAsync(
                tenantId: tenantId,
                userId: userId,
                action: action,
                entityName: entityName,
                entityId: entityId,
                summary: summary,
                ipAddress: ipAddress,
                userAgent: userAgent,
                data: data,
                ct: ct);
        }
    }
}