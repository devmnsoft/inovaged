namespace InovaGed.Application.PhysicalArchive.Productivity;
public static class ArchiveProductivityStatus { public const string Draft="DRAFT",Submitted="SUBMITTED",Approved="APPROVED",Rejected="REJECTED",Cancelled="CANCELLED"; }
public static class ArchiveBillingPeriodStatus { public const string Open="OPEN",InReview="IN_REVIEW",Closed="CLOSED",Approved="APPROVED"; }
public static class ArchiveCalculationRule { public const string Manual="MANUAL",PerItem="PER_ITEM",PerPage="PER_PAGE",PerBox="PER_BOX",PerPrint="PER_PRINT",PerWorkOrder="PER_WORK_ORDER",PerInventoryItem="PER_INVENTORY_ITEM"; }
public sealed class ArchiveServiceCatalogItem
{
    public ArchiveServiceCatalogItem() { }
    public ArchiveServiceCatalogItem(Guid Id, string Code, string Name, string? Description, string Unit, decimal UnitPrice, decimal DefaultUstQuantity, string CalculationRule, bool IsActive, int DisplayOrder)
    {
        this.Id = Id;
        this.Code = Code;
        this.Name = Name;
        this.Description = Description;
        this.Unit = Unit;
        this.UnitPrice = UnitPrice;
        this.DefaultUstQuantity = DefaultUstQuantity;
        this.CalculationRule = CalculationRule;
        this.IsActive = IsActive;
        this.DisplayOrder = DisplayOrder;
    }
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal DefaultUstQuantity { get; set; }
    public string CalculationRule { get; set; } = "";
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}
public sealed record ArchiveServiceCatalogCommand(string Code,string Name,string? Description,string Unit,decimal UnitPrice,decimal DefaultUstQuantity,string CalculationRule,int DisplayOrder=0);
public sealed record ArchiveProductivityRegisterCommand(Guid TenantId,Guid ServiceCatalogId,Guid UserId,DateOnly CompetenceMonth,decimal Quantity,decimal? UstQuantity=null,DateTimeOffset? PerformedAt=null,Guid? TeamId=null,Guid? SectorId=null,string? SourceType=null,Guid? SourceId=null,string? SubjectType=null,Guid? SubjectId=null,Guid? BoxId=null,Guid? DocumentId=null,Guid? WorkOrderId=null,Guid? InventorySessionId=null,Guid? LabelPrintHistoryId=null,string? Notes=null);
public sealed record ArchiveProductivityFilter(DateOnly? CompetenceMonth=null,string? Status=null,Guid? PerformedBy=null,string? SourceType=null);
public sealed class ArchiveProductivityEntryItem
{
    public ArchiveProductivityEntryItem() { }
    public ArchiveProductivityEntryItem(Guid Id, Guid ServiceCatalogId, string ServiceCode, string ServiceName, DateOnly CompetenceMonth, DateTimeOffset PerformedAt, Guid? PerformedBy, string? SourceType, Guid? SourceId, decimal Quantity, decimal UstQuantity, decimal UnitPrice, decimal TotalAmount, string Status, string? Notes, string? RejectionReason)
    {
        this.Id = Id;
        this.ServiceCatalogId = ServiceCatalogId;
        this.ServiceCode = ServiceCode;
        this.ServiceName = ServiceName;
        this.CompetenceMonth = CompetenceMonth;
        this.PerformedAt = PerformedAt;
        this.PerformedBy = PerformedBy;
        this.SourceType = SourceType;
        this.SourceId = SourceId;
        this.Quantity = Quantity;
        this.UstQuantity = UstQuantity;
        this.UnitPrice = UnitPrice;
        this.TotalAmount = TotalAmount;
        this.Status = Status;
        this.Notes = Notes;
        this.RejectionReason = RejectionReason;
    }
    public Guid Id { get; set; }
    public Guid ServiceCatalogId { get; set; }
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public DateOnly CompetenceMonth { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
    public Guid? PerformedBy { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UstQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "";
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
}
public sealed class ArchiveBillingLine
{
    public ArchiveBillingLine() { }
    public ArchiveBillingLine(Guid ServiceCatalogId, string Code, string Service, decimal Quantity, decimal UstQuantity, decimal UnitPrice, decimal TotalAmount, int Entries)
    {
        this.ServiceCatalogId = ServiceCatalogId;
        this.Code = Code;
        this.Service = Service;
        this.Quantity = Quantity;
        this.UstQuantity = UstQuantity;
        this.UnitPrice = UnitPrice;
        this.TotalAmount = TotalAmount;
        this.Entries = Entries;
    }
    public Guid ServiceCatalogId { get; set; }
    public string Code { get; set; } = "";
    public string Service { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UstQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public int Entries { get; set; }
}
public sealed record ArchiveBillingPreview(DateOnly CompetenceMonth,string Status,int TotalEntries,decimal TotalQuantity,decimal TotalUst,decimal TotalAmount,int Pending,IReadOnlyList<ArchiveBillingLine> Lines);
public interface IArchiveServiceCatalogService { Task<IReadOnlyList<ArchiveServiceCatalogItem>> ListAsync(Guid tenantId,CancellationToken ct);Task<Guid>CreateAsync(Guid tenantId,ArchiveServiceCatalogCommand command,CancellationToken ct);Task UpdateAsync(Guid tenantId,Guid id,ArchiveServiceCatalogCommand command,CancellationToken ct);Task ActivateAsync(Guid tenantId,Guid id,CancellationToken ct);Task DeactivateAsync(Guid tenantId,Guid id,CancellationToken ct); }
public interface IArchiveProductivityService { Task<Guid>RegisterAsync(ArchiveProductivityRegisterCommand command,CancellationToken ct);Task RegisterFromWorkOrderAsync(Guid tenantId,Guid workOrderId,Guid userId,CancellationToken ct);Task RegisterFromLabelPrintAsync(Guid tenantId,Guid printHistoryId,Guid userId,CancellationToken ct);Task RegisterFromInventoryAsync(Guid tenantId,Guid inventorySessionId,Guid userId,CancellationToken ct);Task<IReadOnlyList<ArchiveProductivityEntryItem>>ListAsync(Guid tenantId,ArchiveProductivityFilter filter,CancellationToken ct);Task ApproveAsync(Guid tenantId,Guid entryId,Guid userId,CancellationToken ct);Task RejectAsync(Guid tenantId,Guid entryId,Guid userId,string reason,CancellationToken ct); }
public interface IArchiveBillingService { Task<ArchiveBillingPreview>PreviewAsync(Guid tenantId,DateOnly competenceMonth,CancellationToken ct);Task<Guid>OpenPeriodAsync(Guid tenantId,DateOnly competenceMonth,Guid userId,CancellationToken ct);Task ClosePeriodAsync(Guid tenantId,DateOnly competenceMonth,Guid userId,string? notes,CancellationToken ct);Task ApprovePeriodAsync(Guid tenantId,DateOnly competenceMonth,Guid userId,CancellationToken ct); }
