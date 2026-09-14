namespace InovaGed.Application.Labels.Printing;

public static class LabelPrintJobStatus
{
    public const string Pending = "PENDING";
    public const string Previewed = "PREVIEWED";
    public const string ReadyToPrint = "READY_TO_PRINT";
    public const string PdfGenerated = "PDF_GENERATED";
    public const string Printed = "PRINTED";
    public const string Cancelled = "CANCELLED";
    public const string Error = "ERROR";
}

public static class LabelPrintStatusDisplay
{
    public static string Humanize(string status) => status switch
    {
        LabelPrintJobStatus.Pending => "Aguardando preparação",
        LabelPrintJobStatus.Previewed => "Prévia conferida",
        LabelPrintJobStatus.ReadyToPrint => "Pronta para impressão",
        LabelPrintJobStatus.PdfGenerated => "Artefato preparado",
        LabelPrintJobStatus.Printed => "Impressa",
        LabelPrintJobStatus.Cancelled => "Cancelada",
        LabelPrintJobStatus.Error => "Com erro",
        _ => "Status indisponível"
    };
}

public sealed class LabelCanvasPrintSnapshotDto
{
    public string LayoutSource { get; init; } = "CANVAS";
    public string TemplateKey { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public int TemplateVersion { get; init; }
    public string LayoutHash { get; init; } = "";
    public string VersionHash { get; init; } = "";
    public string SubjectType { get; init; } = "";
    public Guid SubjectId { get; init; }
    public object? Branding { get; init; }
    public object? Calibration { get; init; }
    public IReadOnlyDictionary<string,object?> ResolvedValues { get; init; } = new Dictionary<string,object?>();
    public string? TraceCode { get; init; }
    public string? QrPayload { get; init; }
    public int Copies { get; init; } = 1;
    public string ArtifactHash { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public sealed record LabelPrintJobCreateCommand(Guid TenantId, Guid RequestedBy, string PrintMode,
    string TemplateCode, string? TemplateName, string SubjectType, Guid? SubjectId, string? ControlNumber,
    string? Location, int Copies, string PayloadJson, string? ReprintReason, string? IpAddress, string? UserAgent,
    Guid? ClientActionId = null, string OperationType = "NEW_LABEL_CURRENT_DATA");
public sealed record LabelPrintBatchItem(Guid? SubjectId, string SubjectType, string? ControlNumber,
    string? Location, string PayloadJson, int DisplayOrder);
public sealed record LabelPrintBatchJobCreateCommand(Guid TenantId, Guid RequestedBy, string PrintMode,
    string TemplateCode, string? TemplateName, string SubjectType, int Copies,
    IReadOnlyList<LabelPrintBatchItem> Items, string? ReprintReason, string? IpAddress, string? UserAgent,
    Guid? ClientActionId = null);
public sealed class LabelPrintJobFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? UserId { get; set; }
    public string? TemplateCode { get; set; }
    public string? SubjectType { get; set; }
    public string? ControlNumber { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
    public bool? IsReprint { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
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
    string? ReprintReason, string? RequestedByName, IReadOnlyList<LabelPrintJobItemDetails> Items,
    string? ArtifactSha256 = null, string? ArtifactContentType = null, long? ArtifactSizeBytes = null,
    DateTime? ArtifactGeneratedAt = null, string? OperationType = null);
public sealed record LabelPrintQueueMetrics(int Awaiting, int Ready, int PrintedToday, int Errors, int Reprints, int Total);
public sealed record LabelPdfResult(byte[] Content, string ContentType, string FileName, bool IsNativePdf);

public interface ILabelPrintJobService
{
    Task<Guid> CreateJobAsync(LabelPrintJobCreateCommand command, CancellationToken ct);
    Task<Guid> CreateBatchJobAsync(LabelPrintBatchJobCreateCommand command, CancellationToken ct);
    Task<LabelPrintJobDetails?> GetAsync(Guid tenantId, Guid jobId, CancellationToken ct);
    Task<IReadOnlyList<LabelPrintJobListItem>> ListAsync(Guid tenantId, LabelPrintJobFilter filter, CancellationToken ct);
    Task<LabelPrintQueueMetrics> GetMetricsAsync(Guid tenantId, LabelPrintJobFilter filter, CancellationToken ct);
    Task MarkPreviewedAsync(Guid tenantId, Guid jobId, Guid userId, CancellationToken ct);
    Task MarkPrintedAsync(Guid tenantId, Guid jobId, Guid userId, CancellationToken ct);
    Task CancelAsync(Guid tenantId, Guid jobId, Guid userId, string reason, CancellationToken ct);
    Task MarkErrorAsync(Guid tenantId, Guid jobId, string message, CancellationToken ct);
    Task RetryAsync(Guid tenantId, Guid jobId, Guid userId, CancellationToken ct);
    Task<Guid> ReprintExactAsync(Guid tenantId, Guid jobId, Guid userId, Guid clientActionId, string reason, CancellationToken ct);
}

public interface ILabelPdfRenderService
{
    Task<LabelPdfResult> GeneratePdfAsync(Guid tenantId, Guid jobId, CancellationToken ct);
}
