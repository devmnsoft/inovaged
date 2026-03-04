namespace InovaGed.Application.Ged.Batches;

public sealed class BatchRowDto
{
    public Guid Id { get; set; }
    public int BatchNo { get; set; }
    public string Status { get; set; } = "";
    public string? Notes { get; set; } // <- pode ser null no banco
    public DateTime CreatedAt { get; set; }
    public int ItemsCount { get; set; }

    // ✅ obrigatório pro Dapper (forma mais simples)
    public BatchRowDto() { }
}

public sealed class BatchCreateVM
{
    public string BatchNo { get; set; } = "";
    public string? Notes { get; set; }

    public string? Status { get; set; }

    // opcional: inserir já com documentos
    public List<Guid> DocumentIds { get; set; } = new();
    public Guid? BoxId { get; set; }
}

public sealed record BatchItemDto(
    Guid DocumentId,
    Guid? BoxId,
    string? DocumentCode,
    string? DocumentTitle);

public sealed record BatchHistoryDto(
    DateTimeOffset ChangedAt,
    string? FromStatus,
    string ToStatus,
    string? Notes);