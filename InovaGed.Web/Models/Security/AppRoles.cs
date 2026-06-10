using System.Security.Claims;

namespace InovaGed.Web.Security;

public static class AppRoles
{
    public const string Admin = "ADMIN";
    public const string Administrador = "ADMINISTRADOR";
    public const string AdministradorOphir = "ADMINISTRADOROPHIR";
    public const string ArquivistaOphir = "ARQUIVISTAOPHIR";
    public const string Hospital = "HOSPITAL";

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

public static class RolePolicyHelper
{
    public static bool IsFullAdmin(ClaimsPrincipal user)
        => HasRole(user, AppRoles.Admin) || HasRole(user, AppRoles.Administrador);

    public static bool IsOphirManager(ClaimsPrincipal user)
        => HasRole(user, AppRoles.AdministradorOphir);

    public static bool IsOphirArchivist(ClaimsPrincipal user)
        => HasRole(user, AppRoles.ArquivistaOphir);

    public static bool IsHospital(ClaimsPrincipal user)
        => HasRole(user, AppRoles.Hospital);

    private static bool HasRole(ClaimsPrincipal? user, string role)
    {
        if (user is null) return false;
        var target = AppRoles.NormalizeKey(role);
        return user.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Any(c => AppRoles.NormalizeKey(c.Value) == target);
    }
}
