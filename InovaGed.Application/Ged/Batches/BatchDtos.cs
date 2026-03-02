namespace InovaGed.Application.Ged.Batches;

public sealed record BatchRowDto(
    Guid Id,
    string BatchNo,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    int ItemsCount);

public sealed class BatchCreateVM
{
    public string BatchNo { get; set; } = "";
    public string? Notes { get; set; }

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