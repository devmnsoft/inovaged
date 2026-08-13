namespace InovaGed.Application.HospitalBilling;

public sealed record HospitalBillingFilter(string? Insurer = null, string? Competence = null, string? Status = null, bool? HasDenial = null, string? Term = null);
public sealed record HospitalBillingKpis(int Total, int Pending, int Approved, int Divergent, int WithDenial, decimal Presented, decimal ApprovedAmount, decimal Denied, decimal InAppeal, decimal Recovered, int WithoutOcr, int LowConfidence);
public sealed class HospitalBillingDocumentDto
{
    public Guid Id { get; init; } public Guid DocumentId { get; init; } public string Title { get; init; } = "Documento";
    public string DocumentType { get; init; } = "Conta hospitalar"; public string? Insurer { get; init; } public string? Provider { get; init; }
    public string? GuideNumber { get; init; } public string? AuthorizationNumber { get; init; } public string? Competence { get; init; }
    public string? ProviderCnpj { get; init; } public string? Cnes { get; init; } public string? BatchNumber { get; init; }
    public string? InvoiceNumber { get; init; } public string? ProcedureName { get; init; } public string? ProcedureCode { get; init; }
    public string MaskedPatient { get; init; } = "Dado protegido"; public decimal PresentedAmount { get; init; } public decimal ApprovedAmount { get; init; }
    public decimal DeniedAmount { get; init; } public decimal RecoveredAmount { get; init; } public decimal Confidence { get; init; }
    public string Status { get; init; } = "PENDING_REVIEW"; public string? DenialReason { get; init; } public DateOnly? DueDate { get; init; }
    public string? DenialStatus { get; init; } public bool AppealFiled { get; init; } public string DivergenceAlerts { get; init; } = "[]";

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
public sealed record HospitalBillingReportRow(string Label, int Documents, decimal Presented, decimal Approved, decimal Denied, decimal Recovered)
{
    public decimal PendingRecovery => Math.Max(0, Denied - Recovered);
}
public sealed record HospitalBillingReports(
    IReadOnlyList<HospitalBillingReportRow> ByInsurer,
    IReadOnlyList<HospitalBillingReportRow> ByCompetence,
    IReadOnlyList<HospitalBillingReportRow> ByProvider,
    IReadOnlyList<HospitalBillingReportRow> ByReviewStatus,
    IReadOnlyList<HospitalBillingReportRow> Denials);
public sealed record HospitalBillingRuleDto(string DocumentType, string Icon, string[] Signals, string[] RequiredFields, string ReviewGuidance);
public sealed record HospitalBillingRulesCatalog(IReadOnlyList<HospitalBillingRuleDto> Rules, string[] DivergenceChecks);
public interface IHospitalBillingQueries
{
    Task<HospitalBillingDashboard> DashboardAsync(Guid tenantId, HospitalBillingFilter filter, CancellationToken ct);
    Task<HospitalBillingDocumentDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task<HospitalBillingReports> ReportsAsync(Guid tenantId, CancellationToken ct);
}
