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

public sealed class BatchHistoryDto
{
    public DateTime ChangedAt { get; set; }
    public string ToStatus { get; set; } = "";

    public string FromStatus { get; set; } = "";
    public string Notes { get; set; } = "";

    // ✅ Dapper gosta de construtor vazio (mais seguro)
    public BatchHistoryDto() { }

    // ✅ E aqui está o construtor EXATO que o Dapper está pedindo
    // (case-insensitive: changedat/tostatus/notes)
    public BatchHistoryDto(DateTime changedat, string tostatus, string notes)
    {
        ChangedAt = changedat;
        ToStatus = tostatus ?? "";
        Notes = notes ?? "";
    }
}