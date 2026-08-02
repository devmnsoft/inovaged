namespace InovaGed.Web.Services;

public sealed class AtlasIconRegistry : IAtlasIconRegistry
{
    private static readonly IReadOnlyCollection<AtlasIconDefinition> Definitions = new AtlasIconDefinition[]
    {
        new("dashboard", "atlas-icon-dashboard", "Navegação", "dashboard", "outline", Array.Empty<string>()),
        new("workspace", "atlas-icon-workspace", "Navegação", "workspace", "outline", Array.Empty<string>()),
        new("activity", "atlas-icon-activity", "Navegação", "activity", "outline", Array.Empty<string>()),
        new("recent", "atlas-icon-recent", "Navegação", "recent", "outline", Array.Empty<string>()),
        new("notification", "atlas-icon-notification", "Navegação", "notification", "outline", Array.Empty<string>()),
        new("documents", "atlas-icon-documents", "Documentos", "documents", "outline", Array.Empty<string>()),
        new("document", "atlas-icon-document", "Documentos", "document", "outline", Array.Empty<string>()),
        new("document-add", "atlas-icon-document-add", "Documentos", "document add", "outline", Array.Empty<string>()),
        new("document-search", "atlas-icon-document-search", "Documentos", "document search", "outline", Array.Empty<string>()),
        new("document-download", "atlas-icon-document-download", "Documentos", "document download", "outline", Array.Empty<string>()),
        new("document-version", "atlas-icon-document-version", "Documentos", "document version", "outline", Array.Empty<string>()),
        new("document-history", "atlas-icon-document-history", "Documentos", "document history", "outline", Array.Empty<string>()),
        new("document-move", "atlas-icon-document-move", "Documentos", "document move", "outline", Array.Empty<string>()),
        new("document-link", "atlas-icon-document-link", "Documentos", "document link", "outline", Array.Empty<string>()),
        new("preview", "atlas-icon-preview", "Documentos", "preview", "outline", Array.Empty<string>()),
        new("metadata", "atlas-icon-metadata", "Documentos", "metadata", "outline", Array.Empty<string>()),
        new("folder", "atlas-icon-folder", "Pastas", "folder", "outline", Array.Empty<string>()),
        new("folder-open", "atlas-icon-folder-open", "Pastas", "folder open", "outline", Array.Empty<string>()),
        new("folder-add", "atlas-icon-folder-add", "Pastas", "folder add", "outline", Array.Empty<string>()),
        new("folder-move", "atlas-icon-folder-move", "Pastas", "folder move", "outline", Array.Empty<string>()),
        new("folder-favorite", "atlas-icon-folder-favorite", "Pastas", "folder favorite", "outline", Array.Empty<string>()),
        new("search", "atlas-icon-search", "Busca", "search", "outline", Array.Empty<string>()),
        new("smart-search", "atlas-icon-smart-search", "Busca", "smart search", "outline", Array.Empty<string>()),
        new("filter", "atlas-icon-filter", "Busca", "filter", "outline", Array.Empty<string>()),
        new("sort", "atlas-icon-sort", "Busca", "sort", "outline", Array.Empty<string>()),
        new("saved-view", "atlas-icon-saved-view", "Busca", "saved view", "outline", Array.Empty<string>()),
        new("upload", "atlas-icon-upload", "Upload", "upload", "outline", Array.Empty<string>()),
        new("upload-cloud", "atlas-icon-upload-cloud", "Upload", "upload cloud", "outline", Array.Empty<string>()),
        new("upload-pause", "atlas-icon-upload-pause", "Upload", "upload pause", "outline", Array.Empty<string>()),
        new("upload-resume", "atlas-icon-upload-resume", "Upload", "upload resume", "outline", Array.Empty<string>()),
        new("upload-retry", "atlas-icon-upload-retry", "Upload", "upload retry", "outline", Array.Empty<string>()),
        new("drag", "atlas-icon-drag", "Upload", "drag", "outline", Array.Empty<string>()),
        new("grip", "atlas-icon-grip", "Upload", "grip", "outline", Array.Empty<string>()),
        new("ocr", "atlas-icon-ocr", "OCR", "ocr", "outline", Array.Empty<string>()),
        new("ocr-pending", "atlas-icon-ocr-pending", "OCR", "ocr pending", "outline", Array.Empty<string>()),
        new("ocr-success", "atlas-icon-ocr-success", "OCR", "ocr success", "outline", Array.Empty<string>()),
        new("ocr-error", "atlas-icon-ocr-error", "OCR", "ocr error", "outline", Array.Empty<string>()),
        new("classification", "atlas-icon-classification", "Classificação", "classification", "outline", Array.Empty<string>()),
        new("protocol", "atlas-icon-protocol", "Protocolos", "protocol", "outline", Array.Empty<string>()),
        new("protocol-add", "atlas-icon-protocol-add", "Protocolos", "protocol add", "outline", Array.Empty<string>()),
        new("loan", "atlas-icon-loan", "Empréstimos", "loan", "outline", Array.Empty<string>()),
        new("signature", "atlas-icon-signature", "Assinaturas", "signature", "outline", Array.Empty<string>()),
        new("audit", "atlas-icon-audit", "Auditoria", "audit", "outline", Array.Empty<string>()),
        new("report", "atlas-icon-report", "Auditoria", "report", "outline", Array.Empty<string>()),
        new("health", "atlas-icon-health", "Auditoria", "health", "outline", Array.Empty<string>()),
        new("database", "atlas-icon-database", "Auditoria", "database", "outline", Array.Empty<string>()),
        new("users", "atlas-icon-users", "Usuários", "users", "outline", new[] { "user" }),
        new("roles", "atlas-icon-roles", "Usuários", "roles", "outline", Array.Empty<string>()),
        new("settings", "atlas-icon-settings", "Configurações", "settings", "outline", Array.Empty<string>()),
        new("assistant", "atlas-icon-assistant", "Inteligência", "assistant", "outline", Array.Empty<string>()),
        new("assistant-sources", "atlas-icon-assistant-sources", "Inteligência", "assistant sources", "outline", Array.Empty<string>()),
        new("columns", "atlas-icon-columns", "Ações", "columns", "outline", Array.Empty<string>()),
        new("table", "atlas-icon-table", "Ações", "table", "outline", Array.Empty<string>()),
        new("list", "atlas-icon-list", "Ações", "list", "outline", Array.Empty<string>()),
        new("cards", "atlas-icon-cards", "Ações", "cards", "outline", Array.Empty<string>()),
        new("zoom-in", "atlas-icon-zoom-in", "Ações", "zoom in", "outline", Array.Empty<string>()),
        new("zoom-out", "atlas-icon-zoom-out", "Ações", "zoom out", "outline", Array.Empty<string>()),
        new("fullscreen", "atlas-icon-fullscreen", "Ações", "fullscreen", "outline", Array.Empty<string>()),
        new("more", "atlas-icon-more", "Ações", "more", "outline", Array.Empty<string>()),
        new("edit", "atlas-icon-edit", "Ações", "edit", "outline", Array.Empty<string>()),
        new("delete", "atlas-icon-delete", "Ações", "delete", "outline", Array.Empty<string>()),
        new("copy", "atlas-icon-copy", "Ações", "copy", "outline", Array.Empty<string>()),
        new("close", "atlas-icon-close", "Ações", "close", "outline", Array.Empty<string>()),
        new("chevron-down", "atlas-icon-chevron-down", "Ações", "chevron down", "outline", Array.Empty<string>()),
        new("chevron-right", "atlas-icon-chevron-right", "Ações", "chevron right", "outline", Array.Empty<string>()),
        new("arrow-left", "atlas-icon-arrow-left", "Ações", "arrow left", "outline", Array.Empty<string>()),
        new("arrow-right", "atlas-icon-arrow-right", "Ações", "arrow right", "outline", Array.Empty<string>()),
        new("check", "atlas-icon-check", "Estados", "check", "outline", new[] { "success" }),
        new("warning", "atlas-icon-warning", "Estados", "warning", "outline", Array.Empty<string>()),
        new("error", "atlas-icon-error", "Estados", "error", "outline", Array.Empty<string>()),
        new("info", "atlas-icon-info", "Estados", "info", "outline", Array.Empty<string>()),
        new("circle-question", "atlas-icon-circle-question", "Estados", "circle question", "outline", Array.Empty<string>()),
        new("missing", "atlas-icon-missing", "Estados", "missing", "outline", Array.Empty<string>()),
        new("favorite", "atlas-icon-favorite", "Estados", "favorite", "filled", Array.Empty<string>()),
        new("file-pdf", "atlas-icon-file-pdf", "Arquivos", "file pdf", "filled", Array.Empty<string>()),
        new("file-doc", "atlas-icon-file-doc", "Arquivos", "file doc", "filled", Array.Empty<string>()),
        new("file-docx", "atlas-icon-file-docx", "Arquivos", "file docx", "filled", Array.Empty<string>()),
        new("file-xls", "atlas-icon-file-xls", "Arquivos", "file xls", "filled", Array.Empty<string>()),
        new("file-xlsx", "atlas-icon-file-xlsx", "Arquivos", "file xlsx", "filled", Array.Empty<string>()),
        new("file-ppt", "atlas-icon-file-ppt", "Arquivos", "file ppt", "filled", Array.Empty<string>()),
        new("file-pptx", "atlas-icon-file-pptx", "Arquivos", "file pptx", "filled", Array.Empty<string>()),
        new("file-txt", "atlas-icon-file-txt", "Arquivos", "file txt", "filled", Array.Empty<string>()),
        new("file-csv", "atlas-icon-file-csv", "Arquivos", "file csv", "filled", Array.Empty<string>()),
        new("file-jpg", "atlas-icon-file-jpg", "Arquivos", "file jpg", "filled", Array.Empty<string>()),
        new("file-png", "atlas-icon-file-png", "Arquivos", "file png", "filled", Array.Empty<string>()),
        new("file-tiff", "atlas-icon-file-tiff", "Arquivos", "file tiff", "filled", Array.Empty<string>()),
        new("file-zip", "atlas-icon-file-zip", "Arquivos", "file zip", "filled", Array.Empty<string>()),
        new("file-dicom", "atlas-icon-file-dicom", "Arquivos", "file dicom", "filled", Array.Empty<string>()),
        new("file-generic", "atlas-icon-file-generic", "Arquivos", "file generic", "filled", new[] { "file" })
    };

    private static readonly IReadOnlyDictionary<string, AtlasIconDefinition> Lookup = BuildLookup();

    public bool TryGet(string name, out AtlasIconDefinition definition) =>
        Lookup.TryGetValue(name?.Trim() ?? string.Empty, out definition!);

    public IReadOnlyCollection<AtlasIconDefinition> GetAll() => Definitions;

    private static IReadOnlyDictionary<string, AtlasIconDefinition> BuildLookup()
    {
        var result = new Dictionary<string, AtlasIconDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in Definitions)
        {
            result[definition.Name] = definition;
            foreach (var alias in definition.Aliases) result[alias] = definition;
        }
        return result;
    }
}
