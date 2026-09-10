namespace InovaGed.Application.Labels.Canvas;

public sealed record ManualLabelInstance(
    Guid Id,
    Guid TenantId,
    string TemplateKey,
    int TemplateVersion,
    IReadOnlyDictionary<string, string?> Values,
    Guid? BrandingProfileId,
    string Status,
    Guid? CreatedBy = null,
    DateTime? CreatedAt = null,
    Guid? UpdatedBy = null,
    DateTime? UpdatedAt = null,
    DateTime? PrintedAt = null,
    DateTime? ArchivedAt = null,
    Guid? ArchivedBy = null,
    string? Name = null,
    string? ControlNumber = null);

public interface IManualLabelInstanceService
{
    IReadOnlyList<LabelCanvasFieldDto> EditableFields(LabelCanvasDesignDto design);
    LabelCanvasValidationResult Validate(LabelCanvasDesignDto design, IReadOnlyDictionary<string, string?> values);
    Task<ManualLabelInstance> SaveDraftAsync(Guid tenantId, Guid userId, LabelCanvasDesignDto design, IReadOnlyDictionary<string, string?> values, Guid? brandingProfileId, CancellationToken cancellationToken = default);
    Task<ManualLabelInstance> CreateDraftAsync(Guid tenantId, Guid userId, string? name, LabelCanvasDesignDto design, IReadOnlyDictionary<string, string?> values, Guid? brandingProfileId, CancellationToken cancellationToken = default);
    Task<ManualLabelInstance> UpdateDraftAsync(Guid tenantId, Guid userId, Guid id, string? name, LabelCanvasDesignDto design, IReadOnlyDictionary<string, string?> values, Guid? brandingProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManualLabelInstance>> ListAsync(Guid tenantId, string? status = null, CancellationToken cancellationToken = default);
    Task<ManualLabelInstance?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<ManualLabelInstance> DuplicateAsync(Guid tenantId, Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<ManualLabelInstance> ArchiveAsync(Guid tenantId, Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task MarkPrintedAsync(Guid tenantId, Guid userId, Guid id, string? reprintReason = null, CancellationToken cancellationToken = default);
}
