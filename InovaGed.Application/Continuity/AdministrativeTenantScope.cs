using System.Security.Claims;

namespace InovaGed.Application.Continuity;

public interface IAdministrativeTenantScopeResolver
{
    AdministrativeTenantScope Resolve(ClaimsPrincipal user, Guid? requestedTenantId);
}

public sealed record AdministrativeTenantScope(bool Allowed, Guid? TenantId, bool IsGlobal, string? DenialReason);
