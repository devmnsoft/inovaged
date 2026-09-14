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
    public string Sort { get; set; } = "newest";
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
public sealed record LabelPrintJobActionDto(string Key, string Label, bool IsPrimary = false, bool IsDanger = false);
public sealed record LabelPrintJobTimelineDto(string Key, string Title, DateTime At, string User, string Detail, bool IsCurrent = false, bool IsError = false);
public sealed record LabelArtifactIntegrityResult(bool IsValid, string Message);

public static class LabelCalibrationCalculator
{
    public static bool TrySuggestScale(string? measuredText, out decimal percent)
    {
        percent = 0;
        var normalized = measuredText?.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var measured) || measured <= 0) return false;
        percent = decimal.Round(100m * 50m / measured, 2, MidpointRounding.AwayFromZero);
        return percent is >= 80m and <= 120m;
    }
}

/// <summary>Presentation policy derived from the lifecycle authority; Razor never recreates transition rules.</summary>
public static class LabelPrintJobPresentation
{
    private static readonly LabelPrintStateMachine StateMachine = new();

    public static IReadOnlyList<LabelPrintJobActionDto> Actions(string status, bool hasArtifact)
    {
        var actions = new List<LabelPrintJobActionDto>();
        if (status == LabelPrintJobStatus.Pending) actions.Add(new("preview", "Abrir prévia", true));
        else if (status == LabelPrintJobStatus.Previewed) actions.Add(new("prepare", "Preparar impressão", true));
        else if (status == LabelPrintJobStatus.ReadyToPrint) actions.Add(new("artifact", "Gerar artefato", true));
        else if (StateMachine.CanMarkPrinted(status)) actions.Add(new("mark-printed", "Confirmar impressão", true));
        else if (status == LabelPrintJobStatus.Printed)
        {
            actions.Add(new("trace", "Ver rastreabilidade", true));
            actions.Add(new("reprint", "Reimprimir"));
        }
        else if (StateMachine.CanRetry(status)) actions.Add(new("retry", "Tentar novamente", true));
        if (hasArtifact) actions.Add(new("download", "Baixar artefato"));
        if (StateMachine.CanCancel(status)) actions.Add(new("cancel", "Cancelar impressão", false, true));
        return actions;
    }

    public static IReadOnlyList<LabelPrintJobTimelineDto> Timeline(LabelPrintJobDetails job)
    {
        var user = job.RequestedByName ?? "Usuário não informado";
        var events = new List<LabelPrintJobTimelineDto>
        {
            new("created", "Criado", job.RequestedAt, user, "Solicitação de impressão registrada.")
        };
        if (job.ArtifactGeneratedAt is DateTime artifactAt)
        {
            events.Add(new("previewed", "Prévia conferida", artifactAt, user, "Conteúdo validado antes da geração."));
            events.Add(new("ready", "Pronto para impressão", artifactAt, user, "Preparação concluída."));
            events.Add(new("artifact", "Artefato gerado", artifactAt, user, "Arquivo imutável armazenado com SHA-256."));
        }
        else if (job.Status == LabelPrintJobStatus.Previewed)
            events.Add(new("previewed", "Prévia conferida", job.RequestedAt, user, "Conteúdo validado."));
        else if (job.Status == LabelPrintJobStatus.ReadyToPrint)
            events.Add(new("ready", "Pronto para impressão", job.RequestedAt, user, "Preparação concluída."));
        if (job.Status == LabelPrintJobStatus.Error)
            events.Add(new("error", "Erro", job.ArtifactGeneratedAt ?? job.RequestedAt, user, "A operação requer nova tentativa.", true, true));
        if (job.Status == LabelPrintJobStatus.Cancelled)
            events.Add(new("cancelled", "Cancelamento", job.RequestedAt, user, job.CancelReason ?? "Operação cancelada.", true));
        if (job.PrintedAt is DateTime printedAt)
            events.Add(new("printed", "Impressão confirmada", printedAt, user, "Emissão física auditada."));
        if (job.OperationType == "REPRINT_EXACT" || !string.IsNullOrWhiteSpace(job.ReprintReason))
            events.Add(new("reprint", "Reimpressão", job.RequestedAt, user, "Nova emissão governada por motivo obrigatório."));
        var current = events.Count - 1;
        return events.Select((item, index) => item with { IsCurrent = item.IsCurrent || index == current }).ToList();
    }
}

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
    Task<LabelArtifactIntegrityResult> VerifyArtifactAsync(Guid tenantId, Guid jobId, CancellationToken ct);
}

public interface ILabelPdfRenderService
{
    Task<LabelPdfResult> GeneratePdfAsync(Guid tenantId, Guid jobId, CancellationToken ct);
}
