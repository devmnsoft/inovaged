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
    public string? RequestNo { get; set; }
    public string? DeliveryMode { get; set; }
    public DateTime? SlaDueAt { get; set; }
    public string? AdminResponse { get; set; }
    public string? DeliveryInstructions { get; set; }
    public string? RequestDescription { get; set; }
    public string? Priority { get; set; }
}


public sealed record LoanItemDto(
    Guid? DocumentId,
    bool IsPhysical,
    string? DocumentCode,
    string? DocumentTitle,
    string? DocumentType,
    bool IsManual,
    string? ReferenceCode,
    string? Description,
    string? PatientName,
    string? MedicalRecordNumber,
    string? BoxCode,
    string? PhysicalLocation,
    string? Notes);

public sealed class LoanCreateVM
{
    public string RequesterName { get; set; } = "";
    public Guid? RequesterId { get; set; }
    public string? RequesterProfile { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public bool IsPhysical { get; set; }
    public bool AllowDigitalFileAccess { get; set; }

    // seleção de documentos GED ou itens manuais (mínimo 1 no total)
    public List<Guid> DocumentIds { get; set; } = new();
    public List<LoanManualItemVM> ManualItems { get; set; } = new();
    public List<Guid> AllowedFileIds { get; set; } = new();
    public string? Notes { get; set; }
    public string? RequestDescription { get; set; }
    public string? DeliveryMode { get; set; } = "DIGITAL";
    public string? Priority { get; set; } = "NORMAL";
    public string? RequesterContact { get; set; }
    public string? RequesterSectorName { get; set; }
    public string? Justification { get; set; }
}

public sealed class LoanManualItemVM
{
    public string? ReferenceCode { get; set; }
    public string? Description { get; set; }
    public string? DocumentType { get; set; }
    public string? PatientName { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? BoxCode { get; set; }
    public string? PhysicalLocation { get; set; }
    public string? Notes { get; set; }
    public string? RequestDescription { get; set; }
    public string? DeliveryMode { get; set; } = "DIGITAL";
    public string? Priority { get; set; } = "NORMAL";
    public string? RequesterContact { get; set; }
    public string? RequesterSectorName { get; set; }
    public string? Justification { get; set; }
}

public sealed class LoanDetailsVM
{
    public LoanRowDto Header { get; set; } = default!;
    public List<LoanItemDto> Items { get; set; } = new();
    public List<LoanEventDto> History { get; set; } = new();
    public List<LoanMessageDto> Messages { get; set; } = new();
    public bool HistorySchemaMissing { get; set; }
}

public sealed class LoanEventDto
{
    public DateTime EventTime { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ByUserName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? Reason { get; set; }
    public string? InternalNotes { get; set; }
    public string? Sector { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class LoanMessageDto
{
    public DateTime CreatedAt { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
}
