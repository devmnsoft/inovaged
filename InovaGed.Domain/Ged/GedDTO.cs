namespace InovaGed.Domain.Ged
{
    public sealed class CreateFolderCommand
    {
        public Guid? ParentId { get; init; }
        public string Name { get; init; } = "";
        public Guid? DepartmentId { get; init; }
    }
     
    public sealed class DocumentRowDto
    {
        public Guid Id { get; init; }
        public Guid FolderId { get; init; }
        public string Title { get; init; } = "";
        public string? TypeName { get; init; }
        public string? FileName { get; init; }
        public Guid? CurrentVersionId { get; init; }
        public long SizeBytes { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UploadedAtUtc { get; init; }
        public string UploadedAtLocalFormatted { get; set; } = "";
        public Guid CreatedBy { get; set; }   // ou Guid?
        public string? OcrStatus { get; init; }
        public DateTime? OcrFinishedAt { get; init; }
        public bool HasOcrText { get; init; }
        public bool IsOcrAvailable { get; init; }
        public bool IsPartialDocument { get; init; }
        public Guid? PartialGroupId { get; init; }
        public int? PartialPartNumber { get; init; }
        public int? PartialTotalParts { get; init; }
        public string PartialStatus { get; init; } = "NOT_PARTIAL";
        public bool IsDocumentIncomplete { get; init; }
        public int? PartNumber { get; init; }
        public int? TotalParts { get; init; }
        public Guid? ConsolidatedVersionId { get; init; }
        public int PartialPartsCount { get; init; }
        public string PartialStatusLabel => PartialStatusLabels.GetLabel(PartialStatus);
        public string PartialStatusCss => PartialStatusLabels.GetCss(PartialStatus);
         
        public bool IsConfidential { get; init; }
    }

    public sealed class DocumentVersionDto
    {
        public Guid Id { get; init; }
        public int VersionNumber { get; init; }
        public string FileName { get; init; } = "";
        public string? ContentType { get; init; }
        public long SizeBytes { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UploadedAtUtc { get; init; }
        public string UploadedAtLocalFormatted { get; set; } = "";
        public Guid? CreatedBy { get; init; }
        public bool IsCurrent { get; init; }
        public bool HasOcrText { get; init; }
        public bool IsOcrAvailable { get; init; }
        public bool IsPartialDocument { get; init; }
        public Guid? PartialGroupId { get; init; }
        public int? PartialPartNumber { get; init; }
        public int? PartialTotalParts { get; init; }
        public string PartialStatus { get; init; } = "NOT_PARTIAL";
        public bool IsDocumentIncomplete { get; init; }
        public int? PartNumber { get; init; }
        public int? TotalParts { get; init; }
        public Guid? ConsolidatedVersionId { get; init; }
        public int PartialPartsCount { get; init; }
        public string PartialStatusLabel => PartialStatusLabels.GetLabel(PartialStatus);
        public string PartialStatusCss => PartialStatusLabels.GetCss(PartialStatus);

        public string? FileExtension { get; init; }
        public string OcrStatus { get; set; }
        public long? OcrJobId { get; set; }
        public string OcrErrorMessage { get; set; }
        public DateTime? OcrRequestedAt { get; set; }
        public DateTime? OcrStartedAt { get; set; }
        public DateTime? OcrFinishedAt { get; set; }
        public bool OcrInvalidateDigitalSignatures { get; set; }
        public string? OcrText { get; set; }
    }

    public sealed class DocumentVersionDownloadDto
    {
        public Guid VersionId { get; init; }
        public Guid DocumentId { get; init; }
        public string FileName { get; init; } = "";
        public string? ContentType { get; init; }
        public string StoragePath { get; init; } = "";
        public long SizeBytes { get; init; }
    }


    internal static class PartialStatusLabels
    {
        public static string GetLabel(string? status) => (status ?? "NOT_PARTIAL").Trim().ToUpperInvariant() switch
        {
            "INCOMPLETE" => "Documento incompleto",
            "COMPLETE" => "Partes completas",
            "CONSOLIDATED" => "Documento consolidado",
            "CANCELLED" => "Fracionamento cancelado",
            _ => string.Empty
        };

        public static string GetCss(string? status) => (status ?? "NOT_PARTIAL").Trim().ToUpperInvariant() switch
        {
            "INCOMPLETE" => "ged-badge-warning",
            "COMPLETE" => "ged-badge-info",
            "CONSOLIDATED" => "ged-badge-success",
            "CANCELLED" => "ged-badge-muted",
            _ => string.Empty
        };
    }

}
