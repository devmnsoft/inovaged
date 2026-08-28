using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.ContractMeasurement;

public sealed record ContractMeasurementDashboard(string CurrentPeriod, decimal GrossAmount, decimal GlosaAmount, decimal NetAmount, int PendingEntries, int MeasuredItems, int PendingAcceptances, int ExportedReports, bool Ready = true);
public sealed record ContractServiceCatalogItem(Guid Id, string ServiceCode, string Description, string Nature, string UnitName, decimal UnitValue, bool Active);
public sealed record ContractProductivityItem(Guid Id, Guid ServiceId, string Service, string? SourceType, decimal Quantity, decimal UnitValue, decimal TotalValue, string? PerformedByName, DateTimeOffset PerformedAt, string Status);
public sealed record ContractProductivityFilter(Guid TenantId, int? Month = null, int? Year = null, Guid? ServiceId = null, string? User = null, string? Source = null, string? Status = null, string? Search = null);
public sealed record ContractProductivityCreateCommand(Guid TenantId, Guid ServiceId, string? SourceType, Guid? SourceId, Guid? DocumentId, Guid? BoxId, [Range(typeof(decimal), "0.01", "999999999999")] decimal Quantity, [Range(typeof(decimal), "0", "999999999999")] decimal UnitValue, Guid UserId, string? UserName, DateTimeOffset PerformedAt, string? Notes, bool EvidenceRequired);
public sealed record ContractMeasurementPeriodItem(Guid Id, string PeriodCode, int ReferenceMonth, int ReferenceYear, string Status, decimal GrossAmount, decimal GlosaAmount, decimal NetAmount, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt);
public sealed record ContractMeasurementItem(Guid Id, Guid ServiceId, string Description, string Nature, decimal UnitValue, decimal Quantity, decimal GrossAmount, decimal GlosaAmount, decimal NetAmount, string Status);
public sealed record ContractEvidenceItem(Guid Id, Guid? ItemId, string? SourceType, string Title, string? Description, string? EvidenceHash, string? RegisteredByName, DateTimeOffset RegisteredAt);
public sealed record ContractGlosaItem(Guid Id, Guid PeriodId, Guid? ItemId, string Number, string Reason, decimal Amount, string Status, DateTimeOffset OpenedAt, DateTimeOffset? ResolvedAt);
public sealed record ContractAcceptanceEvent(string EventType, string Title, string? Description, string? PerformedByName, DateTimeOffset OccurredAt);
public sealed record ContractMeasurementPeriodDetails(ContractMeasurementPeriodItem Period, IReadOnlyList<ContractMeasurementItem> Items, IReadOnlyList<ContractEvidenceItem> Evidence, IReadOnlyList<ContractGlosaItem> Glosas, IReadOnlyList<ContractAcceptanceEvent> Events);
public sealed record ContractMeasurementPeriodCreateCommand(Guid TenantId, int ReferenceMonth, int ReferenceYear, Guid UserId, string? Notes);
public sealed record ContractGlosaCreateCommand(Guid TenantId, Guid PeriodId, Guid? ItemId, string Reason, decimal Amount, Guid UserId);

public interface IContractMeasurementDashboardService { Task<ContractMeasurementDashboard> GetDashboardAsync(Guid tenantId, CancellationToken ct); }
public interface IContractServiceCatalogService { Task<IReadOnlyList<ContractServiceCatalogItem>> ListAsync(Guid tenantId, CancellationToken ct); }
public interface IContractProductivityService { Task<IReadOnlyList<ContractProductivityItem>> ListAsync(ContractProductivityFilter filter, CancellationToken ct); Task<Guid> CreateAsync(ContractProductivityCreateCommand command, CancellationToken ct); }
public interface IContractMeasurementPeriodService {
 Task<IReadOnlyList<ContractMeasurementPeriodItem>> ListAsync(Guid tenantId,CancellationToken ct); Task<ContractMeasurementPeriodDetails?> GetAsync(Guid tenantId,Guid periodId,CancellationToken ct); Task<Guid>CreateAsync(ContractMeasurementPeriodCreateCommand command,CancellationToken ct); Task GenerateItemsAsync(Guid tenantId,Guid periodId,Guid userId,CancellationToken ct); Task SubmitAsync(Guid tenantId,Guid periodId,Guid userId,CancellationToken ct); Task ApproveAsync(Guid tenantId,Guid periodId,Guid userId,string?notes,CancellationToken ct); Task RejectAsync(Guid tenantId,Guid periodId,Guid userId,string reason,CancellationToken ct); Task CloseAsync(Guid tenantId,Guid periodId,Guid userId,CancellationToken ct); Task<Guid> RegisterEvidenceAsync(Guid tenantId,Guid periodId,Guid?itemId,string?sourceType,Guid?sourceId,string title,string?description,string?hash,Guid userId,string?userName,CancellationToken ct);
}
public interface IContractGlosaService { Task<IReadOnlyList<ContractGlosaItem>> ListAsync(Guid tenantId,CancellationToken ct); Task<Guid>CreateAsync(ContractGlosaCreateCommand command,CancellationToken ct); Task ResolveAsync(Guid tenantId,Guid glosaId,Guid userId,string notes,CancellationToken ct); }
public interface IContractMeasurementReportService { Task<byte[]> ExportAsync(Guid tenantId,string reportType,CancellationToken ct); }
