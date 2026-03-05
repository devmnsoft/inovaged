namespace InovaGed.Application.Ged.Batches;

public sealed class BatchRowDto
{
    public Guid Id { get; set; }
    public int BatchNo { get; set; }
    public string Status { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ItemsCount { get; set; }

    public BatchRowDto() { }
}

public sealed class BatchCreateVM
{
    public string BatchNo { get; set; } = "";
    public string? Notes { get; set; }
    public string? Status { get; set; }

    public List<Guid> DocumentIds { get; set; } = new();
    public Guid? BoxId { get; set; }
}

// Item 17: BoxLabel adicionado para exibir "Caixa #N — LABEL" em vez do GUID bruto
public sealed class BatchItemDto
{
    public Guid DocumentId { get; set; }
    public Guid? BoxId { get; set; }
    public string? DocumentCode { get; set; }
    public string? DocumentTitle { get; set; }

    // Label legível da caixa (ex.: "Caixa #4 — CX-0004")
    // Null quando o item não está vinculado a nenhuma caixa
    public string? BoxLabel { get; set; }

    public BatchItemDto() { }

    // Compatibilidade com código existente que usa construtor posicional
    public BatchItemDto(Guid documentId, Guid? boxId, string? documentCode, string? documentTitle)
    {
        DocumentId = documentId;
        BoxId = boxId;
        DocumentCode = documentCode;
        DocumentTitle = documentTitle;
    }
}

public sealed class BatchHistoryDto
{
    public DateTime ChangedAt { get; set; }
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public string Notes { get; set; } = "";

    public BatchHistoryDto() { }

    public BatchHistoryDto(DateTime changedat, string tostatus, string notes)
    {
        ChangedAt = changedat;
        ToStatus = tostatus ?? "";
        Notes = notes ?? "";
    }
}