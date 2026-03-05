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

    public string LocationLabel
        => string.Join(" / ", new[] { LocationCode, LocationBuilding, LocationRoom }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
}

// FIX: adicionado BoxNo (campo NOT NULL no banco), Notes corrigido para string?
public sealed class BoxFormVM
{
    public Guid? Id { get; set; }

    /// <summary>Número sequencial da caixa. NOT NULL no banco.</summary>
    public int? BoxNo { get; set; }

    /// <summary>Etiqueta/código da caixa. NOT NULL no banco.</summary>
    public string LabelCode { get; set; } = "";

    public Guid? LocationId { get; set; }

    /// <summary>Observações livres.</summary>
    public string? Notes { get; set; }
}

// DTO para a página de conteúdo da caixa (BoxContents)
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