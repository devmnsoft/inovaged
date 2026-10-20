using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Contracts.Fiscalization;

public static class ContractFiscalizationStatus { public const string Draft="DRAFT", Imported="IMPORTED", InReview="IN_REVIEW", WithGlosa="WITH_GLOSA", AwaitingContractorResponse="AWAITING_CONTRACTOR_RESPONSE", Approved="APPROVED", Rejected="REJECTED", Closed="CLOSED"; }
public static class ContractFiscalizationItemStatus { public const string PendingReview="PENDING_REVIEW", Approved="APPROVED", PartiallyGlossed="PARTIALLY_GLOSSED", FullyGlossed="FULLY_GLOSSED", AwaitingCorrection="AWAITING_CORRECTION", Rejected="REJECTED"; }
public static class ContractGlosaStatus { public const string Open="OPEN", Answered="ANSWERED", Accepted="ACCEPTED", Reverted="REVERTED", Maintained="MAINTAINED"; }

public sealed record ContractFiscalizationCreateCommand(Guid TenantId, DateOnly CompetenceMonth, string? ContractNumber, string? ContractorName, string? ContractingUnit, Guid? FiscalUserId, Guid? ManagerUserId, Guid OpenedBy, string? Notes);
public sealed record ContractFiscalizationFilter(DateOnly? CompetenceMonth=null, string? Status=null);
public sealed class ContractFiscalizationPeriodItem
{
    public ContractFiscalizationPeriodItem() { }
    public ContractFiscalizationPeriodItem(Guid Id, DateOnly CompetenceMonth, string? ContractNumber, string? ContractorName, string? ContractingUnit, Guid? FiscalUserId, string Status, decimal TotalMeasuredAmount, decimal TotalApprovedAmount, decimal TotalGlossedAmount, decimal TotalUst, int TotalItems, DateTimeOffset OpenedAt)
    {
        this.Id = Id;
        this.CompetenceMonth = CompetenceMonth;
        this.ContractNumber = ContractNumber;
        this.ContractorName = ContractorName;
        this.ContractingUnit = ContractingUnit;
        this.FiscalUserId = FiscalUserId;
        this.Status = Status;
        this.TotalMeasuredAmount = TotalMeasuredAmount;
        this.TotalApprovedAmount = TotalApprovedAmount;
        this.TotalGlossedAmount = TotalGlossedAmount;
        this.TotalUst = TotalUst;
        this.TotalItems = TotalItems;
        this.OpenedAt = OpenedAt;
    }
    public Guid Id { get; set; }
    public DateOnly CompetenceMonth { get; set; }
    public string? ContractNumber { get; set; }
    public string? ContractorName { get; set; }
    public string? ContractingUnit { get; set; }
    public Guid? FiscalUserId { get; set; }
    public string Status { get; set; } = "";
    public decimal TotalMeasuredAmount { get; set; }
    public decimal TotalApprovedAmount { get; set; }
    public decimal TotalGlossedAmount { get; set; }
    public decimal TotalUst { get; set; }
    public int TotalItems { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}
public sealed class ContractFiscalizationItemDto
{
    public ContractFiscalizationItemDto() { }
    public ContractFiscalizationItemDto(Guid Id, Guid FiscalizationPeriodId, Guid? ProductivityEntryId, Guid? ServiceCatalogId, string? ServiceCode, string ServiceName, string Unit, decimal MeasuredQuantity, decimal MeasuredUst, decimal UnitPrice, decimal MeasuredAmount, decimal ApprovedQuantity, decimal ApprovedUst, decimal ApprovedAmount, decimal GlossedQuantity, decimal GlossedAmount, string Status, string? SourceType, Guid? SourceId, int EvidenceCount, string? FiscalNotes, string? ContractorNotes, DateTimeOffset? PerformedAt = null, Guid? PerformedBy = null)
    {
        this.Id = Id;
        this.FiscalizationPeriodId = FiscalizationPeriodId;
        this.ProductivityEntryId = ProductivityEntryId;
        this.ServiceCatalogId = ServiceCatalogId;
        this.ServiceCode = ServiceCode;
        this.ServiceName = ServiceName;
        this.Unit = Unit;
        this.MeasuredQuantity = MeasuredQuantity;
        this.MeasuredUst = MeasuredUst;
        this.UnitPrice = UnitPrice;
        this.MeasuredAmount = MeasuredAmount;
        this.ApprovedQuantity = ApprovedQuantity;
        this.ApprovedUst = ApprovedUst;
        this.ApprovedAmount = ApprovedAmount;
        this.GlossedQuantity = GlossedQuantity;
        this.GlossedAmount = GlossedAmount;
        this.Status = Status;
        this.SourceType = SourceType;
        this.SourceId = SourceId;
        this.EvidenceCount = EvidenceCount;
        this.FiscalNotes = FiscalNotes;
        this.ContractorNotes = ContractorNotes;
        this.PerformedAt = PerformedAt;
        this.PerformedBy = PerformedBy;
    }
    public Guid Id { get; set; }
    public Guid FiscalizationPeriodId { get; set; }
    public Guid? ProductivityEntryId { get; set; }
    public Guid? ServiceCatalogId { get; set; }
    public string? ServiceCode { get; set; }
    public string ServiceName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal MeasuredQuantity { get; set; }
    public decimal MeasuredUst { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal MeasuredAmount { get; set; }
    public decimal ApprovedQuantity { get; set; }
    public decimal ApprovedUst { get; set; }
    public decimal ApprovedAmount { get; set; }
    public decimal GlossedQuantity { get; set; }
    public decimal GlossedAmount { get; set; }
    public string Status { get; set; } = "";
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public int EvidenceCount { get; set; }
    public string? FiscalNotes { get; set; }
    public string? ContractorNotes { get; set; }
    public DateTimeOffset? PerformedAt { get; set; } = null;
    public Guid? PerformedBy { get; set; } = null;
}
public sealed class ContractGlosaDto
{
    public ContractGlosaDto() { }
    public ContractGlosaDto(Guid Id, Guid FiscalizationItemId, string GlosaType, string Reason, decimal GlossedQuantity, decimal GlossedAmount, string Status, string? ContractorResponse, DateTimeOffset CreatedAt, string? ResolutionNotes)
    {
        this.Id = Id;
        this.FiscalizationItemId = FiscalizationItemId;
        this.GlosaType = GlosaType;
        this.Reason = Reason;
        this.GlossedQuantity = GlossedQuantity;
        this.GlossedAmount = GlossedAmount;
        this.Status = Status;
        this.ContractorResponse = ContractorResponse;
        this.CreatedAt = CreatedAt;
        this.ResolutionNotes = ResolutionNotes;
    }
    public Guid Id { get; set; }
    public Guid FiscalizationItemId { get; set; }
    public string GlosaType { get; set; } = "";
    public string Reason { get; set; } = "";
    public decimal GlossedQuantity { get; set; }
    public decimal GlossedAmount { get; set; }
    public string Status { get; set; } = "";
    public string? ContractorResponse { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? ResolutionNotes { get; set; }
}
public sealed class ContractEvidenceDto
{
    public ContractEvidenceDto() { }
    public ContractEvidenceDto(Guid Id, Guid? FiscalizationItemId, string EvidenceType, string Title, string? Description, string? ExternalUrl, string? SourceType, Guid? SourceId, DateTimeOffset AttachedAt)
    {
        this.Id = Id;
        this.FiscalizationItemId = FiscalizationItemId;
        this.EvidenceType = EvidenceType;
        this.Title = Title;
        this.Description = Description;
        this.ExternalUrl = ExternalUrl;
        this.SourceType = SourceType;
        this.SourceId = SourceId;
        this.AttachedAt = AttachedAt;
    }
    public Guid Id { get; set; }
    public Guid? FiscalizationItemId { get; set; }
    public string EvidenceType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? ExternalUrl { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public DateTimeOffset AttachedAt { get; set; }
}
public sealed class ContractEventDto
{
    public ContractEventDto() { }
    public ContractEventDto(string EventType, string EventTitle, string? EventDescription, string? FromStatus, string? ToStatus, DateTimeOffset PerformedAt, Guid? PerformedBy)
    {
        this.EventType = EventType;
        this.EventTitle = EventTitle;
        this.EventDescription = EventDescription;
        this.FromStatus = FromStatus;
        this.ToStatus = ToStatus;
        this.PerformedAt = PerformedAt;
        this.PerformedBy = PerformedBy;
    }
    public string EventType { get; set; } = "";
    public string EventTitle { get; set; } = "";
    public string? EventDescription { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
    public Guid? PerformedBy { get; set; }
}
public sealed record ContractFiscalizationPeriodDetails(ContractFiscalizationPeriodItem Period, IReadOnlyList<ContractFiscalizationItemDto> Items, IReadOnlyList<ContractGlosaDto> Glosas, IReadOnlyList<ContractEvidenceDto> Evidences, IReadOnlyList<ContractEventDto> Events);
public sealed record ContractGlosaCreateCommand(Guid TenantId, Guid FiscalizationItemId, Guid UserId, [Required] string GlosaType, [Required] string Reason, decimal GlossedQuantity, decimal GlossedAmount);
public sealed record ContractEvidenceCreateCommand(Guid TenantId, Guid FiscalizationPeriodId, Guid? FiscalizationItemId, Guid UserId, [Required] string EvidenceType, [Required] string Title, string? Description, string? ExternalUrl, string? SourceType, Guid? SourceId);
public sealed record ContractFiscalizationReport(ContractFiscalizationPeriodDetails Details, IReadOnlyList<ContractFiscalizationReportLine> Lines, bool Synthetic);
public sealed record ContractFiscalizationReportLine(int Number, string Description, string? Nature, decimal UnitPrice, decimal Quantity, decimal Ust, decimal TotalAmount, decimal GlossedAmount, decimal ApprovedAmount, string? Origin, string Status, string? FiscalNotes, DateTimeOffset? PerformedAt, Guid? OperatorId, int EvidenceCount);

public interface IContractFiscalizationService {
 Task<Guid>CreatePeriodAsync(ContractFiscalizationCreateCommand command,CancellationToken ct); Task ImportProductivityAsync(Guid tenantId,Guid periodId,Guid userId,CancellationToken ct); Task<ContractFiscalizationPeriodDetails?>GetPeriodAsync(Guid tenantId,Guid periodId,CancellationToken ct); Task<IReadOnlyList<ContractFiscalizationPeriodItem>>ListPeriodsAsync(Guid tenantId,ContractFiscalizationFilter filter,CancellationToken ct); Task<IReadOnlyList<ContractFiscalizationItemDto>>ListItemsAsync(Guid tenantId,Guid periodId,CancellationToken ct); Task ApproveItemAsync(Guid tenantId,Guid itemId,Guid userId,string?notes,CancellationToken ct); Task SubmitForApprovalAsync(Guid tenantId,Guid periodId,Guid userId,CancellationToken ct); Task ApprovePeriodAsync(Guid tenantId,Guid periodId,Guid userId,string?notes,CancellationToken ct); Task RejectPeriodAsync(Guid tenantId,Guid periodId,Guid userId,string reason,CancellationToken ct); Task ClosePeriodAsync(Guid tenantId,Guid periodId,Guid userId,CancellationToken ct); Task<Guid>AttachEvidenceAsync(ContractEvidenceCreateCommand command,CancellationToken ct);
}
public interface IContractGlosaService { Task<Guid>CreateGlosaAsync(ContractGlosaCreateCommand command,CancellationToken ct); Task RespondGlosaAsync(Guid tenantId,Guid glosaId,Guid userId,string response,CancellationToken ct); Task MaintainGlosaAsync(Guid tenantId,Guid glosaId,Guid userId,string notes,CancellationToken ct); Task RevertGlosaAsync(Guid tenantId,Guid glosaId,Guid userId,string notes,CancellationToken ct); }
public interface IContractFiscalizationReportService { Task<ContractFiscalizationReport>GenerateSyntheticReportAsync(Guid tenantId,Guid periodId,CancellationToken ct); Task<ContractFiscalizationReport>GenerateAnalyticalReportAsync(Guid tenantId,Guid periodId,CancellationToken ct); Task<string>ExportCsvAsync(Guid tenantId,Guid periodId,string reportType,CancellationToken ct); }
