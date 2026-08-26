namespace InovaGed.Application.PhysicalArchive;
public sealed record LabelTemplateOption(string Code,string Name,string Mode,string SubjectType,string Description,string ViewName,string Version,bool SupportsBatch,bool AllowsManualFields,bool IsSystemTemplate,Guid? Id=null,bool IsDefault=false);
public interface ILabelTemplateCatalogService {
 bool IsTemporaryCatalog { get; }
 Task<IReadOnlyList<LabelTemplateOption>> GetTemplatesAsync(Guid tenantId,string subjectType,string? mode,CancellationToken ct);
 Task<LabelTemplateOption?> TryGetTemplateAsync(Guid tenantId,string templateCode,CancellationToken ct);
 Task<LabelTemplateOption> GetTemplateAsync(Guid tenantId,string templateCode,CancellationToken ct);
 Task<bool> IsCompatibleAsync(Guid tenantId,string templateCode,string subjectType,CancellationToken ct);
}
