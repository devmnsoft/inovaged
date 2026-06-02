using System.Linq;
using System.Security.Claims;

namespace InovaGed.Web.Security;

public static class AppMenuPolicy
{
    public static bool IsAdmin(ClaimsPrincipal user) => HasRole(user, AppRoles.Admin);
    public static bool IsAdministradorOphir(ClaimsPrincipal user) => HasRole(user, AppRoles.AdministradorOphir);
    public static bool IsArquivistaOphir(ClaimsPrincipal user) => HasRole(user, AppRoles.ArquivistaOphir);
    public static bool IsHospitalUser(ClaimsPrincipal user) => HasRole(user, AppRoles.Hospital);

    public static bool IsHospitalRestricted(ClaimsPrincipal user)
        => !IsAdmin(user) && (IsAdministradorOphir(user) || IsArquivistaOphir(user) || IsHospitalUser(user));

    public static bool CanSeeDashboard(ClaimsPrincipal user)
        => !IsHospitalRestricted(user)
           && (IsAdmin(user) || HasRole(user, AppRoles.Arquivista) || HasRole(user, AppRoles.Operador) || HasRole(user, AppRoles.Gestor) || HasRole(user, AppRoles.Auditor));

    public static bool CanSeeHospitalDocuments(ClaimsPrincipal user)
        => IsAdmin(user)
           || IsAdministradorOphir(user)
           || IsArquivistaOphir(user)
           || IsHospitalUser(user)
           || HasRole(user, AppRoles.Arquivista)
           || HasRole(user, AppRoles.Operador);

    public static bool CanSeeLoans(ClaimsPrincipal user)
        => IsAdmin(user)
           || IsAdministradorOphir(user)
           || (!IsHospitalRestricted(user) && (HasRole(user, AppRoles.Arquivista) || HasRole(user, AppRoles.Gestor) || HasRole(user, AppRoles.Operador)));

    public static bool CanCreateHospitalDocuments(ClaimsPrincipal user)
        => IsAdmin(user) || (!IsHospitalRestricted(user) && IsAdministradorOphir(user));

    public static bool CanSeeReports(ClaimsPrincipal user)
        => !IsHospitalRestricted(user)
           && (IsAdmin(user) || HasRole(user, AppRoles.Arquivista) || HasRole(user, AppRoles.Gestor) || HasRole(user, AppRoles.Auditor) || HasRole(user, AppRoles.Operador));

    public static bool CanSeeAudit(ClaimsPrincipal user)
        => !IsHospitalRestricted(user)
           && (IsAdmin(user) || HasRole(user, AppRoles.Gestor) || HasRole(user, AppRoles.Auditor));

    public static bool CanSeeArchivisticInstruments(ClaimsPrincipal user)
        => !IsHospitalRestricted(user) && (IsAdmin(user) || HasRole(user, AppRoles.Arquivista));

    public static bool CanSeeDigitalSignature(ClaimsPrincipal user)
        => !IsHospitalRestricted(user) && (IsAdmin(user) || HasRole(user, AppRoles.Arquivista));

    public static bool CanSeeManual(ClaimsPrincipal user)
        => IsAdmin(user) || !IsHospitalRestricted(user);

    public static bool HasRole(ClaimsPrincipal? user, string role)
    {
        if (user is null) return false;

        var target = NormalizeRole(role);
        return user.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Any(c => NormalizeRole(c.Value) == target);
    }

    public static string NormalizeRole(string? value)
        => (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
}
