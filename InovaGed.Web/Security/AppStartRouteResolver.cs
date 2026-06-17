using System.Security.Claims;

namespace InovaGed.Web.Security;

public static class AppStartRouteResolver
{
    public const string AdminHome = "/Ged";
    public const string HospitalHome = "/HospitalDocuments";

    public static bool IsFullAdmin(ClaimsPrincipal user)
        => RolePolicyHelper.IsFullAdmin(user);

    public static bool IsOphirOrHospital(ClaimsPrincipal user)
        => user.IsInNormalizedRole(AppRoles.AdministradorOphir)
           || user.IsInNormalizedRole(AppRoles.ArquivistaOphir)
           || user.IsInNormalizedRole(AppRoles.Hospital);

    public static string GetDefaultHome(ClaimsPrincipal user)
    {
        if (IsFullAdmin(user))
            return AdminHome;

        return HospitalHome;
    }

    public static bool IsAllowedReturnUrlForUser(ClaimsPrincipal user, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return false;

        var path = returnUrl.Split('?', '#')[0];
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (IsFullAdmin(user))
            return true;

        if (user.IsInNormalizedRole(AppRoles.ArquivistaOphir))
        {
            return path.StartsWith("/HospitalDocuments", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Loans/New", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/Loans", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Loans/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/ProtocolRequests/My", StringComparison.OrdinalIgnoreCase);
        }

        if (user.IsInNormalizedRole(AppRoles.AdministradorOphir))
        {
            return path.StartsWith("/HospitalDocuments", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/Loans", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Loans/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Protocols/WorkQueue", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Users/Sector", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Users/CreateSectorUser", StringComparison.OrdinalIgnoreCase);
        }

        if (user.IsInNormalizedRole(AppRoles.Hospital))
        {
            return path.StartsWith("/HospitalDocuments", StringComparison.OrdinalIgnoreCase);
        }

        return path.StartsWith("/HospitalDocuments", StringComparison.OrdinalIgnoreCase);
    }
}
