namespace InovaGed.Application.HospitalBilling;

using System.ComponentModel.DataAnnotations;

public sealed record HospitalBillingFilter(
    string? Insurer = null, string? Competence = null, string? Status = null,
    bool? HasDenial = null, string? Term = null, string? Unit = null,
    string? Patient = null, string? DocumentType = null, decimal? MinimumAmount = null,
    decimal? MaximumAmount = null, bool? OcrPending = null, bool? HasDivergence = null);
public sealed class HospitalBillingKpis
{
    public int Total { get; set; } public int Pending { get; set; } public int Approved { get; set; }
    public int Divergent { get; set; } public int WithDenial { get; set; } public decimal Presented { get; set; }
    public decimal ApprovedAmount { get; set; } public decimal Denied { get; set; } public decimal InAppeal { get; set; }
    public decimal Recovered { get; set; } public int WithoutOcr { get; set; } public int LowConfidence { get; set; }
}
public sealed class HospitalBillingDocumentDto
{
    public Guid Id { get; set; } public Guid DocumentId { get; set; } public string Title { get; set; } = "Documento";
    public string DocumentType { get; set; } = "Conta hospitalar"; public string? Insurer { get; set; } public string? Provider { get; set; }
    public string? GuideNumber { get; set; } public string? AuthorizationNumber { get; set; } public string? Competence { get; set; }
    public string? ProviderCnpj { get; set; } public string? Cnes { get; set; } public string? BatchNumber { get; set; }
    public string? InvoiceNumber { get; set; } public string? ProcedureName { get; set; } public string? ProcedureCode { get; set; }
    public string MaskedPatient { get; set; } = "Dado protegido"; public decimal PresentedAmount { get; set; } public decimal ApprovedAmount { get; set; }
    public decimal DeniedAmount { get; set; } public decimal RecoveredAmount { get; set; } public decimal Confidence { get; set; }
    public string Status { get; set; } = "PENDING_REVIEW"; public string? DenialReason { get; set; } public DateOnly? DueDate { get; set; }
    public string? DenialStatus { get; set; } public bool AppealFiled { get; set; } public bool HasOcr { get; set; }
    public string DivergenceAlerts { get; set; } = "[]";

    public int? DaysUntilAppealDue => DueDate is { } due
        ? due.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber
        : null;

    public string AppealDeadlineStatus => DaysUntilAppealDue switch
    {
        null => "NO_DEADLINE",
        < 0 => "OVERDUE",
        <= 3 => "CRITICAL",
        <= 7 => "ATTENTION",
        _ => "ON_TIME"
    };
}
public sealed record HospitalBillingDashboard(HospitalBillingKpis Kpis, IReadOnlyList<HospitalBillingDocumentDto> Documents);
public sealed class HospitalBillingReportRow
{
    public string Label { get; set; } = string.Empty;
    public int Documents { get; set; }
    public decimal Presented { get; set; }
    public decimal Approved { get; set; }
    public decimal Denied { get; set; }
    public decimal Recovered { get; set; }
    public decimal PendingRecovery => Math.Max(0, Denied - Recovered);
}
public sealed record HospitalBillingReports(
    IReadOnlyList<HospitalBillingReportRow> ByInsurer,
    IReadOnlyList<HospitalBillingReportRow> ByCompetence,
    IReadOnlyList<HospitalBillingReportRow> ByProvider,
    IReadOnlyList<HospitalBillingReportRow> ByReviewStatus,
    IReadOnlyList<HospitalBillingReportRow> Denials);
public sealed class HospitalBillingReviewHistoryDto
{
    public DateTime ReviewedAt { get; set; } public Guid ReviewedBy { get; set; }
    public string PreviousStatus { get; set; } = string.Empty; public string Status { get; set; } = string.Empty;
    public string? PreviousDenialStatus { get; set; } public string? DenialStatus { get; set; }
    public decimal ApprovedAmount { get; set; } public decimal DeniedAmount { get; set; } public decimal RecoveredAmount { get; set; }
    public string? Notes { get; set; } public string ChangedFields { get; set; } = "{}";
}
public sealed record HospitalBillingDetails(HospitalBillingDocumentDto Document, IReadOnlyList<HospitalBillingReviewHistoryDto> History);
public sealed class HospitalBillingReviewRequest : IValidatableObject
{
    [Required] public Guid Id { get; set; }
    [Required, RegularExpression("^(PENDING_REVIEW|APPROVED|DIVERGENT|DENIED|APPEAL_IN_REVIEW|RECOVERED|CLOSED)$")] public string Status { get; set; } = "PENDING_REVIEW";
    [RegularExpression("^(OPEN|IN_APPEAL|ANSWERED|RECOVERED|CLOSED)?$")] public string? DenialStatus { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal ApprovedAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal DeniedAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal RecoveredAmount { get; set; }
    [StringLength(500)] public string? DenialReason { get; set; }
    public DateOnly? AppealDueDate { get; set; }
    [Required, StringLength(1000, MinimumLength = 5)] public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DeniedAmount > 0 && string.IsNullOrWhiteSpace(DenialReason))
            yield return new("Informe o motivo da glosa.", [nameof(DenialReason)]);
        if (Status is "DENIED" or "APPEAL_IN_REVIEW" && DeniedAmount <= 0)
            yield return new("O fluxo de glosa exige valor glosado maior que zero.", [nameof(DeniedAmount)]);
        if (Status == "RECOVERED" && RecoveredAmount <= 0)
            yield return new("Informe o valor recuperado.", [nameof(RecoveredAmount)]);
        if (DenialStatus == "IN_APPEAL" && AppealDueDate is null)
            yield return new("Informe o prazo do recurso.", [nameof(AppealDueDate)]);
    }
}
public sealed record HospitalBillingRuleDto(string DocumentType, string Icon, string[] Signals, string[] RequiredFields, string ReviewGuidance);
public sealed record HospitalBillingRulesCatalog(IReadOnlyList<HospitalBillingRuleDto> Rules, string[] DivergenceChecks);
public interface IHospitalBillingQueries
{
    Task<HospitalBillingDashboard> DashboardAsync(Guid tenantId, HospitalBillingFilter filter, CancellationToken ct);
    Task<HospitalBillingDocumentDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task<HospitalBillingDocumentDto?> GetByDocumentIdAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<HospitalBillingDetails?> GetDetailsAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task<bool> ReviewAsync(Guid tenantId, Guid userId, HospitalBillingReviewRequest request, CancellationToken ct);
    Task<HospitalBillingReports> ReportsAsync(Guid tenantId, CancellationToken ct);
}
