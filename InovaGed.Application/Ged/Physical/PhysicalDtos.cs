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
}

public sealed class BoxRowDto
{
    public Guid Id { get; set; }
    public int BoxNo { get; set; }
    public string LabelCode { get; set; } = "";
    public Guid? LocationId { get; set; }

    // Para listagem (join com physical_location)
    public string? LocationBuilding { get; set; }
    public string? LocationRoom { get; set; }

    public string? Notes { get; set; }

    public string LocationLabel
        => string.Join(" / ", new[] { LocationBuilding, LocationRoom }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class BoxFormVM
{
    public Guid? Id { get; set; }
    public string LabelCode { get; set; } = "";
    public string? Description { get; set; }
    public Guid? LocationId { get; set; }
    public object Notes { get; set; }
}