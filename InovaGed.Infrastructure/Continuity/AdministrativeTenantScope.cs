using System.Security.Claims;
using InovaGed.Application.Continuity;

namespace InovaGed.Infrastructure.Continuity;

public interface IAdministrativeTenantScopeResolver
{
    AdministrativeTenantScope Resolve(ClaimsPrincipal user, Guid? requestedTenantId);
}

public sealed record AdministrativeTenantScope(bool Allowed, Guid? TenantId, bool IsGlobal, string? DenialReason);

public sealed class AdministrativeTenantScopeResolver : IAdministrativeTenantScopeResolver
{
    public AdministrativeTenantScope Resolve(ClaimsPrincipal user, Guid? requestedTenantId)
    {
        var isGlobal = user.IsInRole("ADMIN") || user.IsInRole("ADMINISTRADOR") || user.HasClaim("scope", "global-tenants");
        var tenantClaim = user.FindFirst("tenant_id")?.Value;
        var localTenant = Guid.TryParse(tenantClaim, out var parsed) ? parsed : (Guid?)null;
        if (isGlobal) return new(true, requestedTenantId ?? localTenant, true, null);
        if (requestedTenantId.HasValue && localTenant.HasValue && requestedTenantId.Value != localTenant.Value)
            return new(false, localTenant, false, "Acesso cruzado de tenant negado.");
        return new(true, localTenant ?? requestedTenantId, false, null);
    }
}
