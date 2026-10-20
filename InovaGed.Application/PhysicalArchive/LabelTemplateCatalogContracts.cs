namespace InovaGed.Application.PhysicalArchive;
public sealed class LabelTemplateOption
{
    public LabelTemplateOption() { }
    public LabelTemplateOption(string Code, string Name, string Mode, string SubjectType, string Description, string ViewName, string Version, bool SupportsBatch, bool AllowsManualFields, bool IsSystemTemplate, Guid? Id = null, bool IsDefault = false)
    {
        this.Code = Code;
        this.Name = Name;
        this.Mode = Mode;
        this.SubjectType = SubjectType;
        this.Description = Description;
        this.ViewName = ViewName;
        this.Version = Version;
        this.SupportsBatch = SupportsBatch;
        this.AllowsManualFields = AllowsManualFields;
        this.IsSystemTemplate = IsSystemTemplate;
        this.Id = Id;
        this.IsDefault = IsDefault;
    }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public string Description { get; set; } = "";
    public string ViewName { get; set; } = "";
    public string Version { get; set; } = "";
    public bool SupportsBatch { get; set; }
    public bool AllowsManualFields { get; set; }
    public bool IsSystemTemplate { get; set; }
    public Guid? Id { get; set; } = null;
    public bool IsDefault { get; set; } = false;
}
public interface ILabelTemplateCatalogService {
 bool IsTemporaryCatalog { get; }
 Task<IReadOnlyList<LabelTemplateOption>> GetTemplatesAsync(Guid tenantId,string subjectType,string? mode,CancellationToken ct);
 Task<LabelTemplateOption?> TryGetTemplateAsync(Guid tenantId,string templateCode,CancellationToken ct);
 Task<LabelTemplateOption> GetTemplateAsync(Guid tenantId,string templateCode,CancellationToken ct);
 Task<bool> IsCompatibleAsync(Guid tenantId,string templateCode,string subjectType,CancellationToken ct);
}
