namespace InovaGed.Application.Labels.Printing;

public static class LabelPrintJobStatus
{
    public const string Pending = "PENDING";
    public const string Previewed = "PREVIEWED";
    public const string PdfGenerated = "PDF_GENERATED";
    public const string Printed = "PRINTED";
    public const string Cancelled = "CANCELLED";
    public const string Error = "ERROR";
}

public sealed record LabelPrintJobCreateCommand(Guid TenantId, Guid RequestedBy, string PrintMode,
    string TemplateCode, string? TemplateName, string SubjectType, Guid? SubjectId, string? ControlNumber,
    string? Location, int Copies, string PayloadJson, string? ReprintReason, string? IpAddress, string? UserAgent);
public sealed record LabelPrintBatchItem(Guid? SubjectId, string SubjectType, string? ControlNumber,
    string? Location, string PayloadJson, int DisplayOrder);
public sealed record LabelPrintBatchJobCreateCommand(Guid TenantId, Guid RequestedBy, string PrintMode,
    string TemplateCode, string? TemplateName, string SubjectType, int Copies,
    IReadOnlyList<LabelPrintBatchItem> Items, string? ReprintReason, string? IpAddress, string? UserAgent);
public sealed class LabelPrintJobFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? UserId { get; set; }
    public string? TemplateCode { get; set; }
    public string? SubjectType { get; set; }
    public string? ControlNumber { get; set; }
    public string? Status { get; set; }
}
public sealed record LabelPrintJobListItem(Guid Id, string JobNumber, string PrintMode, string TemplateCode,
    string? TemplateName, string SubjectType, string? ControlNumber, int Copies, string Status,
    DateTime RequestedAt, DateTime? PrintedAt, string? RequestedByName, string? ReprintReason, int ItemCount);
public sealed record LabelPrintJobItemDetails(Guid Id, Guid? SubjectId, string SubjectType, string? ControlNumber,
    string? Location, string PayloadJson, string Status, int DisplayOrder, DateTime? PrintedAt, string? ErrorMessage);
public sealed record LabelPrintJobDetails(Guid Id, Guid TenantId, string JobNumber, string PrintMode,
    string TemplateCode, string? TemplateName, string SubjectType, Guid? SubjectId, string? ControlNumber,
    string? Location, int Copies, string Status, string PayloadJson, string? PdfPath, string? ErrorMessage,
    Guid? RequestedBy, DateTime RequestedAt, Guid? PrintedBy, DateTime? PrintedAt, string? CancelReason,
    string? ReprintReason, string? RequestedByName, IReadOnlyList<LabelPrintJobItemDetails> Items);
public sealed record LabelPdfResult(byte[] Content, string ContentType, string FileName, bool IsNativePdf);

public interface ILabelPrintJobService
{
    Task<Guid> CreateJobAsync(LabelPrintJobCreateCommand command, CancellationToken ct);
    Task<Guid> CreateBatchJobAsync(LabelPrintBatchJobCreateCommand command, CancellationToken ct);
    Task<LabelPrintJobDetails?> GetAsync(Guid tenantId, Guid jobId, CancellationToken ct);
    Task<IReadOnlyList<LabelPrintJobListItem>> ListAsync(Guid tenantId, LabelPrintJobFilter filter, CancellationToken ct);
    Task MarkPreviewedAsync(Guid tenantId, Guid jobId, Guid userId, CancellationToken ct);
    Task MarkPrintedAsync(Guid tenantId, Guid jobId, Guid userId, CancellationToken ct);
    Task CancelAsync(Guid tenantId, Guid jobId, Guid userId, string reason, CancellationToken ct);
}

public interface ILabelPdfRenderService
{
    Task<LabelPdfResult> GeneratePdfAsync(Guid tenantId, Guid jobId, CancellationToken ct);
}
