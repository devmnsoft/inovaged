using InovaGed.Application.PhysicalArchive;

namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelTemplateCatalogService : ILabelTemplateCatalogService
{
    private static readonly LabelTemplateOption[] Items =
    [
        new("FACTORY_BOX_V1", "Padrão do Sistema - Caixa", "FACTORY", "BOX", "Etiqueta padrão do InovaGED para caixas físicas.", "BoxLabel", "1", true, false, true),
        new("FACTORY_DOCUMENT_V1", "Padrão do Sistema - Documento/Pasta", "FACTORY", "DOCUMENT", "Etiqueta padrão do InovaGED para documentos e pastas.", "DocumentLabel", "1", true, false, true),
        new("LOCDESK_CAIXA_V1", "LocDesk - Caixa", "CUSTOM", "BOX", "Modelo personalizado LocDesk para identificação de caixas físicas.", "LocDeskBoxLabel", "1", true, true, false),
        new("LOCDESK_PASTA_V1", "LocDesk - Pasta", "CUSTOM", "DOCUMENT", "Modelo personalizado LocDesk para identificação de pastas/documentos.", "LocDeskFolderLabel", "1", true, true, false)
    ];

    public IReadOnlyList<LabelTemplateOption> GetTemplates(string subjectType, string? mode = null) => Items
        .Where(x => x.SubjectType.Equals(subjectType, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(mode) || x.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))).ToArray();
    public LabelTemplateOption GetTemplate(string templateCode) => Items.FirstOrDefault(x => x.Code.Equals(templateCode, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException("Modelo de etiqueta não encontrado.");
    public bool IsCompatible(string templateCode, string subjectType) => Items.Any(x => x.Code.Equals(templateCode, StringComparison.OrdinalIgnoreCase) && x.SubjectType.Equals(subjectType, StringComparison.OrdinalIgnoreCase));
}
