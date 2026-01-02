
namespace InovaGed.Application.Classification;

public sealed class DocumentClassificationDto
{
    public Guid DocumentId { get; set; }

    public Guid? DocumentTypeId { get; set; }
    public string? DocumentTypeName { get; set; }

    public string? Source { get; set; } // MANUAL / AUTO_FOLDER / AUTO_OCR

    public Guid? SuggestedTypeId { get; set; }
    public string? SuggestedTypeName { get; set; }
    public decimal? SuggestedConfidence { get; set; }
    public DateTimeOffset? SuggestedAt { get; set; }

    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}
