namespace InovaGed.Application.PhysicalArchive;

public sealed record LabelTemplateOption(string Code, string Name, string Mode, string SubjectType,
    string Description, string ViewName, string Version, bool SupportsBatch,
    bool AllowsManualFields, bool IsSystemTemplate);

public interface ILabelTemplateCatalogService
{
    IReadOnlyList<LabelTemplateOption> GetTemplates(string subjectType, string? mode = null);
    LabelTemplateOption GetTemplate(string templateCode);
    bool IsCompatible(string templateCode, string subjectType);
}
