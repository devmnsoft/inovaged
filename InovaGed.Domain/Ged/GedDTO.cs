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
        public long SizeBytes { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; set; }   // ou Guid?
         
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
        public Guid? CreatedBy { get; init; }
        public bool IsCurrent { get; init; }

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

}
