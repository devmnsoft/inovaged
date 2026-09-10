namespace InovaGed.Application.Labels.Canvas;

public sealed record ManualLabelInstance(Guid Id, Guid TenantId, string TemplateKey, int TemplateVersion, IReadOnlyDictionary<string,string?> Values, Guid? BrandingProfileId, string Status);
public interface IManualLabelInstanceService
{
    IReadOnlyList<LabelCanvasFieldDto> EditableFields(LabelCanvasDesignDto design);
    LabelCanvasValidationResult Validate(LabelCanvasDesignDto design,IReadOnlyDictionary<string,string?> values);
    Task<ManualLabelInstance> SaveDraftAsync(Guid tenantId,Guid userId,LabelCanvasDesignDto design,IReadOnlyDictionary<string,string?> values,Guid? brandingProfileId,CancellationToken cancellationToken=default);
    Task<ManualLabelInstance?> GetAsync(Guid tenantId,Guid id,CancellationToken cancellationToken=default);
    Task<ManualLabelInstance> DuplicateAsync(Guid tenantId,Guid userId,Guid id,CancellationToken cancellationToken=default);
    Task MarkPrintedAsync(Guid tenantId,Guid userId,Guid id,CancellationToken cancellationToken=default);
}
