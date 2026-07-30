namespace InovaGed.Web.Services;

public interface IIconCatalog
{
    bool TryGetPath(string name, out string pathData);
    IReadOnlyCollection<AppIconDefinition> GetAll();
}

public sealed record AppIconDefinition(string Name, string Category, string Usage, string PathData);

public sealed class IconCatalog : IIconCatalog
{
    private static readonly IReadOnlyDictionary<string, AppIconDefinition> Icons = new[]
    {
        new AppIconDefinition("dashboard", "Navegação", "Painel inicial", "M3 13h8V3H3v10Zm0 8h8v-6H3v6Zm10 0h8V11h-8v10Zm0-18v6h8V3h-8Z"),
        new AppIconDefinition("search", "Busca", "Pesquisar", "M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14Zm5 12 5 5"),
        new AppIconDefinition("assistant", "Chat", "Assistente InovaGED", "M4 4h16v13H8l-4 4V4Zm4 5h8M8 13h5"),
        new AppIconDefinition("notification", "Alertas", "Central de notificações", "M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9Zm-8 12h4"),
        new AppIconDefinition("success", "Estados", "Operação concluída", "M22 11a10 10 0 1 1-5-8M8 11l3 3L22 3"),
        new AppIconDefinition("warning", "Alertas", "Atenção", "M12 3 2 21h20L12 3Zm0 6v5m0 3h.01"),
        new AppIconDefinition("error", "Estados", "Erro", "M8 3h8l5 5v8l-5 5H8l-5-5V8l5-5Zm1 6 6 6m0-6-6 6"),
        new AppIconDefinition("upload", "Upload", "Enviar arquivo", "M12 17V4m0 0L7 9m5-5 5 5M4 16v6h16v-6"),
        new AppIconDefinition("document", "Documentos", "Documento", "M6 2h9l5 5v15H6V2Zm8 2v5h5M9 13h8M9 17h8"),
        new AppIconDefinition("folder", "Pastas", "Pasta", "M2 6h8l2 2h10v13H2V6Z"),
        new AppIconDefinition("ocr", "OCR", "Processamento OCR", "M4 3H2v5m18-5h2v5M4 21H2v-5m18 5h2v-5M7 8h10M7 12h10M7 16h6"),
        new AppIconDefinition("audit", "Administração", "Auditoria", "M5 3h14v18H5V3Zm4 5h6M9 12h6M9 16h4"),
        new AppIconDefinition("user", "Usuários", "Usuário", "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 9c0-4 3-7 7-7s7 3 7 7"),
        new AppIconDefinition("settings", "Administração", "Configurações", "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm0-5v2m0 14v2M3 12h2m14 0h2M5.6 5.6 7 7m10 10 1.4 1.4M18.4 5.6 17 7M7 17l-1.4 1.4"),
        new AppIconDefinition("continuity", "Administração", "Continuidade", "M12 3a9 9 0 1 0 9 9M12 3v5l4-4M8 12a4 4 0 1 0 4-4"),
        new AppIconDefinition("logout", "Ações", "Sair", "M10 4H4v16h6M14 8l4 4-4 4m4-4H8")
    }.ToDictionary(icon => icon.Name, StringComparer.OrdinalIgnoreCase);

    public bool TryGetPath(string name, out string pathData)
    {
        if (Icons.TryGetValue(name, out var icon)) { pathData = icon.PathData; return true; }
        pathData = string.Empty; return false;
    }

    public IReadOnlyCollection<AppIconDefinition> GetAll() => Icons.Values.OrderBy(x => x.Category).ThenBy(x => x.Name).ToArray();
}
