namespace InovaGed.Application.Ocr;

public static class OcrOperationalReason
{
    public const string NotEligible = "não elegível";
    public const string Waiting = "aguardando";
    public const string Processing = "processando";
    public const string Completed = "concluído";
    public const string Failed = "falhou";
    public const string NeedsIntervention = "requer intervenção";

    public static string Present(string? scheduleStatus, string? jobStatus)
    {
        var job = Normalize(jobStatus);
        var schedule = Normalize(scheduleStatus);

        if (job is "FAILED_ENVIRONMENT" or "FAILED_PERMANENT" || schedule is "SKIPPED_ENVIRONMENT" or "ENVIRONMENT_INVALID")
            return NeedsIntervention;
        if (job is "PROCESSING" or "RUNNING")
            return Processing;
        if (job is "PENDING" or "QUEUED")
            return Waiting;
        if (job is "COMPLETED")
            return Completed;
        if (job is "ERROR" or "FAILED" or "FAILURE" || schedule is "FAILED")
            return Failed;
        if (schedule is "QUEUED")
            return Waiting;
        if (schedule is "SKIPPED_ALREADY_HAS_OCR")
            return Completed;
        if (schedule is "SKIPPED_PENDING")
            return Waiting;
        if (schedule is "SKIPPED_PROCESSING")
            return Processing;
        return NotEligible;
    }

    public static string SafeDetail(string situation, string? reason)
    {
        if (situation == NeedsIntervention)
            return "O ambiente de OCR precisa de correção antes de uma nova tentativa.";
        if (situation == Failed)
            return "A rotina não concluiu este documento. Uma nova execução pode tentar de novo.";
        return string.IsNullOrWhiteSpace(reason) ? situation : reason.Trim();
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
}

public static class OcrOperationalState
{
    public const string AvailableWithResults = "disponível com resultados";
    public const string AvailableEmpty = "disponível sem resultados";
    public const string Unavailable = "indisponível";
    public const string AccessDenied = "acesso negado";
}

public sealed class OcrDiagnosticInfo
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string? SqlState { get; set; }
    public string? CorrelationId { get; set; }
    public bool CanRetry { get; set; }
    public string RecoveryAction { get; set; } = string.Empty;
}

public sealed class OcrRunReasonsFilter
{
    public string? Situation { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
}

public sealed class OcrOperationalReasonsQueryResult
{
    public string State { get; set; } = OcrOperationalState.AvailableEmpty;
    public IReadOnlyList<OcrOperationalReasonDto> Items { get; set; } = Array.Empty<OcrOperationalReasonDto>();
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
    public string? SituationFilter { get; set; }
    public OcrDiagnosticInfo? Diagnostics { get; set; }

    public int WaitingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public int NeedsInterventionCount { get; set; }
    public int NotEligibleCount { get; set; }
}

public sealed class OcrOperationalReasonDto
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Situation { get; set; } = OcrOperationalReason.NotEligible;
    public string Detail { get; set; } = string.Empty;
    public string RunItemStatus { get; set; } = string.Empty;
    public string? CurrentJobStatus { get; set; }
    public long? JobId { get; set; }
}
