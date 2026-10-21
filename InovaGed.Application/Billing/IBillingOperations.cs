using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Billing;

public interface IBillingOperations
{
    Task<BillingOperationsDto> GetAsync(Guid tenantId, Guid extractionId, CancellationToken ct);
    Task<IReadOnlyList<BillingAssignee>> AssigneesAsync(Guid tenantId, CancellationToken ct);
    Task<bool> AddInstallmentAsync(Guid tenantId, Guid userId, BillingInstallmentInput input, CancellationToken ct);
    Task<bool> SettleAsync(Guid tenantId, Guid userId, BillingSettlementInput input, CancellationToken ct);
    Task<bool> ReconcileAsync(Guid tenantId, Guid userId, Guid settlementId, CancellationToken ct);
    Task<BillingDuplicatePolicy> DuplicatePolicyAsync(Guid tenantId, CancellationToken ct);
    Task SaveDuplicatePolicyAsync(Guid tenantId, Guid userId, BillingDuplicatePolicy policy, CancellationToken ct);
}
public sealed class BillingDuplicatePolicy
{
    public bool CompareSeries { get; set; } = true;
    public bool CompareCompetence { get; set; } = true;
    public bool CompareAmount { get; set; }
}
public sealed record BillingAssignee(Guid Id, string Name);
public sealed class BillingInstallmentInput
{
    public Guid ExtractionId { get; set; }
    [Required, RegularExpression("^(PAYABLE|RECEIVABLE)$")] public string Direction { get; set; } = "PAYABLE";
    [Range(1, 9999)] public int Sequence { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999.99", ParseLimitsInInvariantCulture = true)] public decimal Amount { get; set; }
    [Required] public DateTime? DueDate { get; set; }
}
public sealed class BillingSettlementInput
{
    public Guid ExtractionId { get; set; }
    public Guid InstallmentId { get; set; }
    public Guid IdempotencyKey { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999.99", ParseLimitsInInvariantCulture = true)] public decimal Amount { get; set; }
    [Required] public DateTime? SettledOn { get; set; }
    [Required, StringLength(200)] public string Reference { get; set; } = "";
}
public sealed class BillingInstallmentDto
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public string Direction { get; set; } = "";
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Settled { get; set; }
    public decimal Balance => Amount - Settled;
    public string Status => Settled == 0 ? "Em aberto" : Balance > 0 ? "Parcial" : "Liquidado";
}
public sealed class BillingSettlementDto
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public decimal Amount { get; set; }
    public DateTime SettledOn { get; set; }
    public string Reference { get; set; } = "";
    public DateTime? ReconciledAt { get; set; }
    public Guid CreatedBy { get; set; }
}
public sealed class BillingHistoryDto
{
    public DateTime ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? Reason { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string? PreviousSnapshotJson { get; set; }
}
public sealed record BillingOperationsDto(IReadOnlyList<BillingInstallmentDto> Installments,
    IReadOnlyList<BillingSettlementDto> Settlements, IReadOnlyList<BillingHistoryDto> History);
