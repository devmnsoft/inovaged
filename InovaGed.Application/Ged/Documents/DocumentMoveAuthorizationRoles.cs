namespace InovaGed.Application.Ged.Documents;

public static class DocumentMoveAuthorizationRoles
{
    public const string Admin = "ADMIN";
    public const string Administrador = "ADMINISTRADOR";
    public const string AdministradorOphir = "ADMINISTRADOROPHIR";

    private static readonly HashSet<string> AdministrativeRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        Admin,
        Administrador,
        AdministradorOphir
    };

    public static bool IsAdministrative(IEnumerable<string> roles)
        => roles.Any(role => AdministrativeRoles.Contains(role));
}
