using System.Security.Claims;
using InovaGed.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace InovaGed.Infrastructure.Common.Security;

public sealed class TenantAccessor : ITenantAccessor
{
    // Ajuste se seu tenant default for sempre o mesmo
    private static readonly Guid DefaultTenant =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid GetTenantIdOrThrow(HttpContext ctx)
        => TryGetTenantId(ctx) ?? DefaultTenant;

    public Guid? TryGetTenantId(HttpContext ctx)
    {
        // 1) Items (caso você já coloque em middleware)
        if (ctx.Items.TryGetValue("TenantId", out var v) && v is Guid g1)
            return g1;

        // 2) Claim (ajuste o nome se seu token/claims usar outro)
        var claim =
            ctx.User?.FindFirst("tenant_id") ??
            ctx.User?.FindFirst("tenantId") ??
            ctx.User?.FindFirst("TenantId");

        if (claim != null && Guid.TryParse(claim.Value, out var g2))
            return g2;

        // 3) Header (multi-tenant via header)
        if (ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var hdr) &&
            Guid.TryParse(hdr.ToString(), out var g3))
            return g3;

        // 4) fallback null
        return null;
    }
}