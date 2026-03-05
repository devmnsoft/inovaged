using Microsoft.AspNetCore.Http;

namespace InovaGed.Application.Common.Security;

public interface ITenantAccessor
{
    Guid GetTenantIdOrThrow(HttpContext ctx);
    Guid? TryGetTenantId(HttpContext ctx);
}