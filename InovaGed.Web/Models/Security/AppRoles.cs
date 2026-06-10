using System.Security.Claims;

namespace InovaGed.Web.Security;

public static class AppRoles
{
    public const string Admin = "ADMIN";
    public const string Administrador = "ADMINISTRADOR";
    public const string AdministradorOphir = "ADMINISTRADOROPHIR";
    public const string ArquivistaOphir = "ARQUIVISTAOPHIR";
    public const string Hospital = "HOSPITAL";

    // Perfis legados mantidos apenas para compatibilidade de dados antigos.
    public const string Arquivista = "ARQUIVISTA";
    public const string Auditor = "AUDITOR";
    public const string Gestor = "GESTOR";
    public const string Operador = "OPERADOR";

    public const string FullAdminRoles = Admin + "," + Administrador;

    public static string Normalize(string? role)
    {
        var value = NormalizeKey(role);

        return value switch
        {
            "ADMIN" => Admin,
            "ADMINISTRADOR" => Administrador,
            "ADMINISTRATOR" => Administrador,
            "ADMINISTRADOROPHIR" => AdministradorOphir,

            "ARQUIVISTA" => Arquivista,
            "ARQUIVISTAOPHIR" => ArquivistaOphir,
            "HOSPITAL" => Hospital,
            "USUARIOHOSPITALAR" => Hospital,
            "USUÁRIOHOSPITALAR" => Hospital,

            "AUDITOR" => Auditor,
            "GESTOR" => Gestor,
            "OPERADOR" => Operador,

            _ => Operador
        };
    }

    internal static string NormalizeKey(string? value)
        => (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
}

public static class AppRoleGroups
{
    public static readonly string[] FullAdmins =
    {
        AppRoles.Admin,
        AppRoles.Administrador
    };

    public static readonly string[] GedUsers =
    {
        AppRoles.Admin,
        AppRoles.Administrador,
        AppRoles.AdministradorOphir,
        AppRoles.ArquivistaOphir
    };

    public static readonly string[] HospitalDocumentUsers =
    {
        AppRoles.Admin,
        AppRoles.Administrador,
        AppRoles.AdministradorOphir,
        AppRoles.ArquivistaOphir,
        AppRoles.Hospital
    };
}

public static class ClaimsPrincipalRoleExtensions
{
    public static bool IsFullAdmin(this ClaimsPrincipal user)
        => user.IsInNormalizedRole(AppRoles.Admin) || user.IsInNormalizedRole(AppRoles.Administrador);

    public static bool IsInNormalizedRole(this ClaimsPrincipal? user, string role)
    {
        if (user is null) return false;
        var target = AppRoles.NormalizeKey(role);
        return user.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Any(c => AppRoles.NormalizeKey(c.Value) == target);
    }
}

public static class RolePolicyHelper
{
    public static bool IsFullAdmin(ClaimsPrincipal user) => user.IsFullAdmin();

    public static bool IsOphirManager(ClaimsPrincipal user)
        => user.IsInNormalizedRole(AppRoles.AdministradorOphir);

    public static bool IsOphirArchivist(ClaimsPrincipal user)
        => user.IsInNormalizedRole(AppRoles.ArquivistaOphir);

    public static bool IsHospital(ClaimsPrincipal user)
        => user.IsInNormalizedRole(AppRoles.Hospital);
}
