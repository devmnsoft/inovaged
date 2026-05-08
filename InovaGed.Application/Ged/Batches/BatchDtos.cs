namespace InovaGed.Application.Ged.Batches;

public sealed class BatchRowDto
{
    public Guid Id { get; set; }
    public int BatchNo { get; set; }
    public string Status { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ItemsCount { get; set; }

    public string StatusLabel => Status switch
    {
        "RECEIVED" => "Recebido",
        "TRIAGE" => "Triagem",
        "DIGITIZATION" => "Digitalização",
        "INDEXING" => "Indexação",
        "ARCHIVED" => "Arquivado",
        _ => Status
    };

    public BatchRowDto() { }
}

public sealed class BatchCreateVM
{
    public string? Notes { get; set; }

    public List<Guid> DocumentIds { get; set; } = new();

    public Guid? BoxId { get; set; }

    public bool HasDocuments => DocumentIds is { Count: > 0 };
}

public sealed class BatchItemDto
{
    public Guid DocumentId { get; set; }
    public Guid? BoxId { get; set; }
    public string? DocumentCode { get; set; }
    public string? DocumentTitle { get; set; }
    public string? BoxLabel { get; set; }

    public BatchItemDto() { }

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
    public string? EventType { get; set; }

    public string EventLabel => EventType switch
    {
        "CREATE" => "Criação do lote",
        "ADD_ITEM" => "Documento adicionado",
        "REMOVE_ITEM" => "Documento removido",
        "MOVE_ITEM_BOX" => "Movimentação de caixa",
        "STATUS_CHANGE" => "Mudança de etapa",
        "IMPORT" => "Importação",
        "EXPORT" => "Exportação",
        _ => EventType ?? ToStatus
    };

    public BatchHistoryDto() { }

    public BatchHistoryDto(DateTime changedat, string tostatus, string notes)
    {
        ChangedAt = changedat;
        ToStatus = tostatus ?? "";
        Notes = notes ?? "";
    }
}