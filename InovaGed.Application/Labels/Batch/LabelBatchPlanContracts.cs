using InovaGed.Application.Labels.Preview;

namespace InovaGed.Application.Labels.Batch;

public sealed record LabelBatchPlanRequest(
    Guid TenantId,
    Guid? UserId,
    string SubjectType,
    IReadOnlyList<Guid> SubjectIds,
    string? TemplateCode,
    string? PrintMode,
    Guid? BrandingProfileId,
    Guid? PrintProfileId,
    int Copies,
    string? ReprintReason = null,
    string? AbsoluteBaseUrl = null);

public sealed record LabelBatchSamplePreview(
    Guid SubjectId,
    string Reference,
    string Html);

public sealed record LabelBatchItemError(
    Guid SubjectId,
    string Reference,
    string Message);

public sealed record LabelBatchPlanResult(
    bool Ok,
    int SelectedCount,
    int AlreadyPrintedCount,
    int PhysicalLabels,
    int LabelsPerPage,
    int PageCount,
    LabelPreviewLayoutInfo Layout,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<LabelBatchSamplePreview> Samples,
    IReadOnlyList<LabelBatchItemError> ItemErrors,
    string? ErrorCode = null,
    string? Message = null);

public interface ILabelBatchPlanService
{
    Task<LabelBatchPlanResult> CalculatePlanAsync(LabelBatchPlanRequest request, CancellationToken cancellationToken = default);
}
