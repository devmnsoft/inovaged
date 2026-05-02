namespace InovaGed.Application.Common.Preview;

public sealed class LibreOfficeOptions
{
    /// <summary>
    /// Caminho completo do soffice.exe.
    /// Exemplo:
    /// C:\Program Files\LibreOffice\program\soffice.exe
    /// </summary>
    public string SofficePath { get; set; } = "";

    /// <summary>
    /// Caminho alternativo usado por versões anteriores da configuração.
    /// Mantido para compatibilidade com appsettings.json antigo.
    /// </summary>
    public string LibreOfficePath { get; set; } = "";

    /// <summary>
    /// Timeout em minutos para conversão.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 2;

    /// <summary>
    /// Timeout em segundos para compatibilidade com configuração anterior.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    public string GetResolvedSofficePath()
    {
        if (!string.IsNullOrWhiteSpace(SofficePath))
            return SofficePath.Trim();

        if (!string.IsNullOrWhiteSpace(LibreOfficePath))
            return LibreOfficePath.Trim();

        var candidates = new[]
        {
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return "";
    }

    public TimeSpan GetTimeout()
    {
        if (TimeoutMinutes > 0)
            return TimeSpan.FromMinutes(TimeoutMinutes);

        if (TimeoutSeconds > 0)
            return TimeSpan.FromSeconds(TimeoutSeconds);

        return TimeSpan.FromMinutes(2);
    }
}