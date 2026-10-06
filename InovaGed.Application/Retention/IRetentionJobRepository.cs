namespace InovaGed.Application.Retention;

public interface IRetentionJobRepository
{
    Task<int> RecalculateAsync(Guid tenantId, int dueSoonDays, CancellationToken ct);
    Task<RetentionDashboardVM> GetDashboardAsync(Guid tenantId, int dueSoonDays, CancellationToken ct);
     
    Task<int> RecalculateOneAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct); 
    Task<int> RecalculateOneAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
        => throw new NotSupportedException("Transactional recalculation is required for durable recovery.");
}

public sealed class RetentionDashboardVM
{
    public int TotalClassified { get; set; }
    public int DueSoon { get; set; }
    public int Overdue { get; set; }
    public int WithoutClassification { get; set; }
}
