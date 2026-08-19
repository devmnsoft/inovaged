namespace InovaGed.Application.Labels;
public interface ILabelTemplateVersioningService {
 Task<int> PublishVersionAsync(Guid tenantId,Guid templateId,Guid userId,string? notes,CancellationToken ct);
 Task<IReadOnlyList<LabelTemplateVersionItem>> ListVersionsAsync(Guid tenantId,Guid templateId,CancellationToken ct);
 Task<LabelTemplateVersionDetails?> GetVersionAsync(Guid tenantId,Guid versionId,CancellationToken ct);
}
