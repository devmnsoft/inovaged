namespace InovaGed.Application.Ged.Loans;

public sealed class LoanRowDto
{
    public Guid Id { get; set; }
    public long ProtocolNo { get; set; }
    public string Status { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReturnedAt { get; set; }

    // ⚠️ TEM que ser int para bater com o erro (System.Int32 itemscount)
    public int ItemsCount { get; set; }
}

public sealed record LoanItemDto(
    Guid DocumentId,
    bool IsPhysical,
    string? DocumentCode,
    string? DocumentTitle,
    string? DocumentType);

public sealed class LoanCreateVM
{
    public string RequesterName { get; set; } = "";
    public Guid? RequesterId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public bool IsPhysical { get; set; } = true;

    // seleção de documentos (mínimo 1)
    public List<Guid> DocumentIds { get; set; } = new();
    public string? Notes { get; set; }
}

public sealed class LoanDetailsVM
{
    public LoanRowDto Header { get; set; } = default!;
    public List<LoanItemDto> Items { get; set; } = new();
    public List<LoanEventDto> History { get; set; } = new();
}

public sealed record LoanEventDto(
    DateTimeOffset EventTime,
    string EventType,
    string? ByUserName,
    string? Notes);