namespace InovaGed.Web.Security;

public static class AppRoles
{
    public const string ArquivistaOphir = "ARQUIVISTAOPHIR";
    public const string AdministradorOphir = "ADMINISTRADOROPHIR";
    public const string Hospital = "HOSPITAL";

    public const string Admin = "ADMIN";
    public const string Arquivista = "ARQUIVISTA";
    public const string Auditor = "AUDITOR";
    public const string Gestor = "GESTOR";
    public const string Operador = "OPERADOR";

    public static string Normalize(string? role)
    {
        var value = (role ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();

        return value switch
        {
            "ADMIN" => Admin,
            "ADMINISTRADOR" => Admin,
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
}
