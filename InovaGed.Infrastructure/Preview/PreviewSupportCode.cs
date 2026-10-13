namespace InovaGed.Infrastructure.Preview;

internal static class PreviewSupportCode
{
    public static string Create() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static string PublicMessage(string supportCode)
        => $"Não foi possível gerar o preview deste arquivo. O original continua disponível para download autorizado. Informe o suporte {supportCode}.";

    public static string JobMessage(string supportCode)
        => $"Falha no processamento do preview. Informe o suporte {supportCode}.";
}
