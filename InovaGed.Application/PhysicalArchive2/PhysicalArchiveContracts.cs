namespace InovaGed.Application.PhysicalArchive2;

public sealed record PhysicalArchiveDashboard(long Boxes, long LabelledBoxes, long UnlocatedBoxes, long LoanedBoxes, long OpenInventories, long OverdueLoans, long MonthlyMovements, long PendingChecks);
public sealed record PhysicalOption(Guid Id, string Code, string Name);
public sealed record PhysicalActivity(Guid Id, string Code, string Title, string Status, string? Location, DateTimeOffset OccurredAt, string? Detail);
public sealed record PhysicalInventoryDetails(Guid Id, string Number, string Title, string Status, Guid? LocationId, string? Location, DateTimeOffset StartedAt, IReadOnlyList<PhysicalActivity> Items);

public interface IPhysicalArchive2Service
{
    Task<PhysicalArchiveDashboard> DashboardAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<PhysicalOption>> BoxesAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<PhysicalOption>> LocationsAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<PhysicalActivity>> MovementsAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<PhysicalActivity>> InventoriesAsync(Guid tenantId, CancellationToken ct);
    Task<PhysicalInventoryDetails?> InventoryAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<PhysicalActivity>> LoansAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<PhysicalActivity>> CustodyAsync(Guid tenantId, Guid boxId, CancellationToken ct);
    Task<Guid> StartInventoryAsync(Guid tenantId, Guid? locationId, string title, Guid? userId, CancellationToken ct);
    Task ScanAsync(Guid tenantId, Guid sessionId, string code, Guid? foundLocationId, Guid? userId, CancellationToken ct);
    Task CloseInventoryAsync(Guid tenantId, Guid sessionId, Guid? userId, string? notes, CancellationToken ct);
    Task MoveAsync(Guid tenantId, Guid boxId, Guid toLocationId, string type, string? reason, Guid? userId, string? userName, CancellationToken ct);
    Task<Guid> LoanAsync(Guid tenantId, Guid boxId, string requester, string? department, string? reason, DateTimeOffset? dueAt, Guid? userId, CancellationToken ct);
    Task ReturnLoanAsync(Guid tenantId, Guid loanId, string? notes, Guid? userId, CancellationToken ct);
}
