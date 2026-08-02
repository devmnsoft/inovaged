namespace InovaGed.Web.Services;

public sealed class AtlasIllustrationRegistry : IAtlasIllustrationRegistry
{
    private static readonly IReadOnlyDictionary<string, AtlasIllustrationDefinition> Definitions =
        new AtlasIllustrationDefinition[]
        {
            Define("login-ged-workspace", "login-ged-workspace.svg", "Login e segurança", false, 640, 480),
            Define("empty-folder", "empty-folder-atlas.svg", "Pasta vazia"),
            Define("empty-search", "empty-search-atlas.svg", "Busca sem resultados"),
            Define("upload-drop", "upload-drop-atlas.svg", "Destino de upload"),
            Define("upload-validating", "upload-validating-atlas.svg", "Validação de upload"),
            Define("upload-processing", "upload-processing-atlas.svg", "Upload em processamento"),
            Define("upload-success", "upload-success-atlas.svg", "Upload concluído"),
            Define("upload-error", "upload-error-atlas.svg", "Erro de upload"),
            Define("upload-duplicate", "upload-duplicate-atlas.svg", "Arquivo duplicado"),
            Define("preview-empty", "preview-empty-atlas.svg", "Preview vazio"),
            Define("preview-loading", "preview-loading-atlas.svg", "Preview carregando"),
            Define("preview-unsupported", "preview-unsupported-atlas.svg", "Preview incompatível"),
            Define("preview-error", "preview-error-atlas.svg", "Erro no preview"),
            Define("preview-restricted", "preview-restricted-atlas.svg", "Preview restrito"),
            Define("favorites-empty", "favorites-empty-atlas.svg", "Favoritos vazios"),
            Define("recents-empty", "recents-empty-atlas.svg", "Recentes vazios"),
            Define("activity-empty", "activity-empty-atlas.svg", "Atividades vazias"),
            Define("notifications-empty", "notifications-empty-atlas.svg", "Notificações vazias"),
            Define("saved-views-empty", "saved-views-empty-atlas.svg", "Visões salvas vazias"),
            Define("work-queue-empty", "work-queue-empty-atlas.svg", "Fila vazia"),
            Define("assistant-sources", "assistant-sources-atlas.svg", "Fontes do assistente")
        }.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string name, out AtlasIllustrationDefinition definition) =>
        Definitions.TryGetValue(name?.Trim() ?? string.Empty, out definition!);

    private static AtlasIllustrationDefinition Define(
        string name,
        string file,
        string usage,
        bool decorative = true,
        int width = 480,
        int height = 360) =>
        new(name, $"/images/atlas/{file}", width, height, usage, decorative);
}
