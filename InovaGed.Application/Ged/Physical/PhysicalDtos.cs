namespace InovaGed.Application.Ged.Physical;

public sealed class PhysicalLocationRowDto
{
    public Guid Id { get; set; }
    public string Building { get; set; } = "";
    public string? Room { get; set; }
    public string? Aisle { get; set; }
    public string? Rack { get; set; }
    public string? Shelf { get; set; }
    public string? Pallet { get; set; }
    public string? Notes { get; set; }
    public string? LocationCode { get; set; }
    public string? PropertyName { get; set; }
    public string? UnitName { get; set; }
    public string? FullLocationCode { get; set; }
    public string RegStatus { get; set; } = "A";

    public string LocationLabel
        => string.Join(" / ", new[] { FullLocationCode ?? LocationCode, Building, Room, Aisle, Rack, Shelf, Pallet }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class PhysicalLocationFormVM
{
    public Guid? Id { get; set; }
    public string? LocationCode { get; set; }
    public string? PropertyName { get; set; }
    public string? AddressStreet { get; set; }
    public string? AddressNumber { get; set; }
    public string? AddressDistrict { get; set; }
    public string? AddressCity { get; set; }
    public string? AddressState { get; set; }
    public string? AddressZip { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }
    public string? Aisle { get; set; }
    public string? Rack { get; set; }
    public string? Shelf { get; set; }
    public string? Pallet { get; set; }
    public string? Notes { get; set; }
    public string? UnitName { get; set; }
}

public sealed class BoxRowDto
{
    public Guid Id { get; set; }
    public int BoxNo { get; set; }
    public string LabelCode { get; set; } = "";
    public Guid? LocationId { get; set; }
    public string? LocationBuilding { get; set; }
    public string? LocationRoom { get; set; }
    public string? LocationCode { get; set; }
    public string? Notes { get; set; }
    public string LifecycleStatus { get; set; } = "OPEN";
    public bool IsFull { get; set; }
    public int DocumentCount { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public DateTime? LastMovedAt { get; set; }
    public Guid? LastMovedBy { get; set; }

    public string LocationLabel
        => string.Join(" / ", new[] { LocationCode, LocationBuilding, LocationRoom }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class BoxFormVM
{
    public Guid? Id { get; set; }
    public int? BoxNo { get; set; }
    public string LabelCode { get; set; } = "";
    public Guid? LocationId { get; set; }
    public string? Notes { get; set; }
    public string LifecycleStatus { get; set; } = "OPEN";
    public bool IsFull { get; set; }
}

public sealed class BoxContentItemDto
{
    public Guid DocumentId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public Guid BatchId { get; set; }
    public string BatchStatus { get; set; } = "";
    public DateTime AddedAt { get; set; }
}

public sealed class AvailableDocumentForBoxDto
{
    public Guid DocumentId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public Guid BatchId { get; set; }
    public string BatchNo { get; set; } = "";
    public string BatchStatus { get; set; } = "";
    public Guid? CurrentBoxId { get; set; }
    public string? CurrentBoxLabel { get; set; }
}

public sealed class BoxContentMaintenanceVM
{
    public Guid BoxId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? BatchId { get; set; }
    public string? Notes { get; set; }
    public bool SpecialPermission { get; set; }
}

public sealed class PhysicalLocationHistoryDto
{
    public DateTime ChangedAt { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public Guid? ChangedBy { get; set; }
}

public sealed class PhysicalMapRowDto
{
    public Guid DocumentId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public Guid BatchId { get; set; }
    public string BatchNo { get; set; } = "";
    public string BatchStatus { get; set; } = "";
    public Guid? BoxId { get; set; }
    public int? BoxNo { get; set; }
    public string? LabelCode { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public string? PropertyName { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }
    public string? Aisle { get; set; }
    public string? Rack { get; set; }
    public string? Shelf { get; set; }
    public string? Pallet { get; set; }
    public string? FullLocation { get; set; }
    public DateTime? LinkedAt { get; set; }
}

public sealed class BoxLocationHistoryRowDto
{
    public DateTime ChangedAt { get; set; }
    public int BoxNo { get; set; }
    public string LabelCode { get; set; } = "";
    public string? OldLocation { get; set; }
    public string? NewLocation { get; set; }
    public string? Notes { get; set; }
}
