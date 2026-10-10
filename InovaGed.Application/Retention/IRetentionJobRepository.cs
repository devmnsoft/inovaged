namespace InovaGed.Application.Retention;

public interface IRetentionJobRepository
{
    Task<int> RecalculateAsync(Guid tenantId, int dueSoonDays, CancellationToken ct);
    Task<RetentionDashboardVM> GetDashboardAsync(Guid tenantId, int dueSoonDays, CancellationToken ct);
     
    Task<int> RecalculateOneAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct); 
    Task<int> RecalculateOneAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
        => throw new NotSupportedException("Transactional recalculation is required for durable recovery.");

    Task<Guid> EnqueueRecalculateAsync(Guid tenantId, Guid documentId, string reason, CancellationToken ct);
    Task<Guid> EnqueueRecalculateAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid documentId, string reason, CancellationToken ct);

    Task<bool> ResolvePendingRecalcAsync(Guid tenantId, Guid pendingId, CancellationToken ct);
    Task<bool> ResolvePendingRecalcAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid pendingId, CancellationToken ct);

    Task<RetentionCalculationMemory?> SimulateCalculationAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
        => Task.FromResult<RetentionCalculationMemory?>(null);
}

public sealed class RetentionCalculationMemory
{
    public Guid DocumentId { get; init; }
    public Guid? ClassificationId { get; init; }
    public string? ClassificationCode { get; init; }
    public string? ClassificationName { get; init; }
    public int? PlanVersionNo { get; init; }
    public string? PlanVersionTitle { get; init; }
    public string? StartEvent { get; init; }
    public DateTime? BasisDate { get; init; }
    public bool IsBasisEventPending { get; init; }
    public int? RetentionActiveDays { get; init; }
    public int? RetentionActiveMonths { get; init; }
    public int? RetentionActiveYears { get; init; }
    public int? RetentionArchiveDays { get; init; }
    public int? RetentionArchiveMonths { get; init; }
    public int? RetentionArchiveYears { get; init; }
    public bool HasExplicitZeroPeriod { get; init; }
    public bool IsRuleIncomplete { get; init; }
    public string? FinalDestination { get; init; }
    public bool IsPermanentRecord { get; init; }
    public bool IsHoldActive { get; init; }
    public DateTime? CalculatedDueAt { get; init; }
    public string CalculatedStatus { get; init; } = "";
    public string FormulaText { get; init; } = "";
    public DateTime? PersistedDueAt { get; init; }
    public string? PersistedStatus { get; init; }
    public DateTime? PersistedBasisAt { get; init; }
    public string? RuleSource { get; init; }
}

public sealed class RetentionDashboardVM
{
    public int TotalClassified { get; set; }
    public int DueSoon { get; set; }
    public int Overdue { get; set; }
    public int WithoutClassification { get; set; }
}
