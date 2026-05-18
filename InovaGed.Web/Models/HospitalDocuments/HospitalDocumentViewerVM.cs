namespace InovaGed.Web.Models.HospitalDocuments;

public sealed class HospitalDocumentViewerVM
{
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }

    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string TypeName { get; set; } = "";

    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public string OcrText { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public string OcrUrl { get; set; } = "";
}