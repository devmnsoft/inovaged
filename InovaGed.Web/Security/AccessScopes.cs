using System.Security.Claims;

namespace InovaGed.Web.Security;

public sealed record LoanScope(
    bool CanSeeAll,
    string? SectorId,
    Guid? UserId,
    bool OnlyOwnRequests,
    bool CanManage);

public sealed record ProtocolScope(
    bool CanSeeAll,
    string? SectorId,
    Guid? UserId,
    bool OnlyOwnRequests,
    bool CanManage);

public static class AccessScopeResolver
{
    public static LoanScope ResolveLoanScope(ClaimsPrincipal user, Guid? userId, string? sectorId)
    {
        var isFullAdmin = RolePolicyHelper.IsFullAdmin(user);
        var isOphirManager = RolePolicyHelper.IsOphirManager(user);
        var isOphirArchivist = RolePolicyHelper.IsOphirArchivist(user);

        return new LoanScope(
            CanSeeAll: isFullAdmin,
            SectorId: isOphirManager ? sectorId : null,
            UserId: userId,
            OnlyOwnRequests: !isFullAdmin && !isOphirManager && isOphirArchivist,
            CanManage: isFullAdmin || isOphirManager);
    }

    public static ProtocolScope ResolveProtocolScope(ClaimsPrincipal user, Guid? userId, string? sectorId)
    {
        var isFullAdmin = RolePolicyHelper.IsFullAdmin(user);
        var isOphirManager = RolePolicyHelper.IsOphirManager(user);
        var isOphirArchivist = RolePolicyHelper.IsOphirArchivist(user);

        return new ProtocolScope(
            CanSeeAll: isFullAdmin,
            SectorId: isOphirManager ? sectorId : null,
            UserId: userId,
            OnlyOwnRequests: !isFullAdmin && !isOphirManager && isOphirArchivist,
            CanManage: isFullAdmin || isOphirManager);
    }
}
