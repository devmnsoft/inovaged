namespace InovaGed.Application.Labels;
public interface ILabelTemplateManager {
 Task<IReadOnlyList<LabelTemplateListItem>> ListAsync(Guid tenantId,CancellationToken ct);
 Task<LabelTemplateDetails?> GetAsync(Guid tenantId,Guid templateId,CancellationToken ct);
 Task<Guid> CreateCustomAsync(Guid tenantId,LabelTemplateEditCommand command,CancellationToken ct);
 Task UpdateAsync(Guid tenantId,Guid templateId,LabelTemplateEditCommand command,CancellationToken ct);
 Task ActivateAsync(Guid tenantId,Guid templateId,CancellationToken ct); Task DeactivateAsync(Guid tenantId,Guid templateId,CancellationToken ct);
 Task SetDefaultAsync(Guid tenantId,Guid templateId,CancellationToken ct);
}
