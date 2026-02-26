namespace InovaGed.Application.Ocr;

public sealed class OcrJobDto
{
    public long Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }

    public string SourcePdfPath { get; init; } = default!; // caminho no storage/local
    public string OriginalFileName { get; init; } = default!;

    public string Language { get; init; } = "por";
    public bool Force { get; init; }

    public DateTime CreatedAt { get; init; }
     
    // seu worker usa DocumentVersionId:
    public Guid DocumentVersionId { get; init; }

    // seu worker usa InvalidateDigitalSignatures:
    public bool InvalidateDigitalSignatures { get; init; }
     
    public Guid? RequestedBy { get; init; } // ✅ NOVO 
    public object TicketId { get; set; }
    public object TicketFileId { get; set; }
    public string InputDiskPath { get; set; }
    public int Priority { get; set; }
}
