namespace InovaGed.Web.Services;

public sealed record UiMessageAction(string Label, string? Url = null, string? Command = null);

public sealed record UiMessageDefinition(
    string Code,
    string Severity,
    string Title,
    string Message,
    string? Guidance,
    string Icon,
    bool Persistent,
    UiMessageAction? Action);

public interface IUiMessageCatalog
{
    UiMessageDefinition Get(string code);
    bool TryGet(string code, out UiMessageDefinition definition);
    IReadOnlyCollection<UiMessageDefinition> GetAll();
}

public sealed class UiMessageCatalog : IUiMessageCatalog
{
    private static readonly IReadOnlyDictionary<string, UiMessageDefinition> Definitions =
        new Dictionary<string, UiMessageDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["GED.FOLDER.CREATED"] = Success("GED.FOLDER.CREATED", "Pasta criada", "A nova pasta está pronta para receber documentos.", "Abra a pasta para adicionar arquivos.", "folder-add", "Abrir pasta"),
            ["GED.FOLDER.RENAMED"] = Success("GED.FOLDER.RENAMED", "Pasta renomeada", "O novo nome já aparece na árvore de pastas.", null, "edit"),
            ["GED.FOLDER.MOVED"] = Success("GED.FOLDER.MOVED", "Pasta movida", "A pasta foi adicionada ao destino escolhido.", "Confira o novo caminho na árvore.", "folder-move", "Abrir destino"),
            ["GED.FOLDER.DELETED"] = Success("GED.FOLDER.DELETED", "Pasta excluída", "A pasta foi removida do acervo.", null, "delete"),
            ["GED.DOCUMENT.UPLOADED"] = Success("GED.DOCUMENT.UPLOADED", "Documentos enviados", "Os arquivos foram adicionados à pasta selecionada.", "Abra a pasta para revisar classificação e OCR.", "upload", "Abrir pasta"),
            ["GED.DOCUMENT.MOVED"] = Success("GED.DOCUMENT.MOVED", "Documentos movidos", "Os documentos agora estão na pasta de destino.", null, "document-move", "Abrir destino"),
            ["GED.DOCUMENT.CLASSIFIED"] = Success("GED.DOCUMENT.CLASSIFIED", "Classificação aplicada", "A classificação documental foi atualizada.", "Revise a temporalidade quando necessário.", "classification"),
            ["GED.DOCUMENT.METADATA_UPDATED"] = Success("GED.DOCUMENT.METADATA_UPDATED", "Metadados atualizados", "As novas informações foram salvas no documento.", null, "metadata"),
            ["GED.OCR.REQUESTED"] = Info("GED.OCR.REQUESTED", "OCR solicitado", "O documento entrou na fila de extração de texto.", "Você pode continuar trabalhando enquanto o processamento ocorre.", "ocr-pending"),
            ["GED.OCR.COMPLETED"] = Success("GED.OCR.COMPLETED", "OCR concluído", "O texto pesquisável está disponível para o documento.", null, "ocr-success", "Pesquisar texto"),
            ["GED.OCR.FAILED"] = Error("GED.OCR.FAILED", "Não foi possível concluir o OCR", "O arquivo não pôde ser processado nesta tentativa.", "Tente novamente ou confira a qualidade do arquivo.", "ocr-error", "Tentar novamente"),
            ["GED.BULK.PARTIAL_SUCCESS"] = Warning("GED.BULK.PARTIAL_SUCCESS", "Alguns documentos precisam de atenção", "Parte dos itens foi processada e parte requer revisão.", "Abra o resultado para ver os itens ignorados ou com erro.", "warning", "Ver resultado", true),
            ["GED.SEARCH.EMPTY"] = Info("GED.SEARCH.EMPTY", "Nenhum documento encontrado", "Não há itens que correspondam aos termos e filtros atuais.", "Remova um filtro ou pesquise com outras palavras.", "search"),
            ["GED.PREVIEW.UNAVAILABLE"] = Warning("GED.PREVIEW.UNAVAILABLE", "Preview indisponível", "Este arquivo não pode ser visualizado no navegador agora.", "Baixe o arquivo para abri-lo no aplicativo adequado.", "preview", "Baixar arquivo"),
            ["GED.PERMISSION.DENIED"] = Error("GED.PERMISSION.DENIED", "Acesso não permitido", "Seu perfil não possui permissão para concluir esta ação.", "Solicite acesso ao responsável pelo acervo.", "warning", null, true)
        };

    public UiMessageDefinition Get(string code) =>
        TryGet(code, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Mensagem de interface não cadastrada: {code}");

    public bool TryGet(string code, out UiMessageDefinition definition) => Definitions.TryGetValue(code, out definition!);

    public IReadOnlyCollection<UiMessageDefinition> GetAll() => Definitions.Values.ToArray();

    private static UiMessageDefinition Success(string code, string title, string message, string? guidance, string icon, string? action = null) =>
        Create(code, "success", title, message, guidance, icon, action);

    private static UiMessageDefinition Info(string code, string title, string message, string? guidance, string icon, string? action = null) =>
        Create(code, "info", title, message, guidance, icon, action);

    private static UiMessageDefinition Warning(string code, string title, string message, string? guidance, string icon, string? action = null, bool persistent = false) =>
        Create(code, "warning", title, message, guidance, icon, action, persistent);

    private static UiMessageDefinition Error(string code, string title, string message, string? guidance, string icon, string? action = null, bool persistent = true) =>
        Create(code, "error", title, message, guidance, icon, action, persistent);

    private static UiMessageDefinition Create(string code, string severity, string title, string message, string? guidance, string icon, string? action, bool persistent = false) =>
        new(code, severity, title, message, guidance, icon, persistent, action is null ? null : new UiMessageAction(action));
}
