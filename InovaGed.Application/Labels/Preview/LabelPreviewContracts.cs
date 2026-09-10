namespace InovaGed.Application.Labels.Preview;

public sealed record LabelPreviewTemplateInfo(
    string Key,
    string Name,
    string? Version,
    string Type);

public sealed record LabelPreviewBrandingInfo(
    Guid? ProfileId,
    string? ProfileName,
    string? Client,
    string? Contract,
    string? Organization);

public sealed record LabelPreviewLayoutInfo(
    decimal LabelWidthMm,
    decimal LabelHeightMm,
    string Paper,
    string Orientation,
    int Columns,
    int Rows,
    int LabelsPerPage,
    int EstimatedPages);

public sealed record LabelPreviewReprintInfo(
    bool AlreadyPrinted,
    bool ReasonRequired);

public sealed record LabelQuickPreviewResult(
    bool Ok,
    string? Html = null,
    IReadOnlyList<string>? Warnings = null,
    string? ErrorCode = null,
    string? Message = null,
    LabelPreviewTemplateInfo? Template = null,
    LabelPreviewBrandingInfo? Branding = null,
    LabelPreviewLayoutInfo? Layout = null,
    LabelPreviewReprintInfo? Reprint = null);

public sealed record LabelQuickPreviewCommand(
    Guid TenantId,
    Guid? UserId,
    string SubjectType,
    Guid? SubjectId,
    string? TemplateCode,
    string? PrintMode,
    Guid? BrandingProfileId,
    Guid? PrintProfileId,
    Guid? SelectedLogoAssetId,
    int Copies,
    string? ReprintReason,
    IReadOnlyDictionary<string, string?>? ManualValues = null,
    string? AbsoluteBaseUrl = null);

public interface ILabelPreviewService
{
    Task<LabelQuickPreviewResult> GenerateQuickPreviewAsync(LabelQuickPreviewCommand command, CancellationToken cancellationToken = default);
}
