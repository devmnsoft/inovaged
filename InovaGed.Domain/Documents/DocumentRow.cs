namespace InovaGed.Domain.Documents
{

    public sealed class DocumentRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public Guid? FolderId { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? TypeId { get; set; }
        public Guid? ClassificationId { get; set; }
        public string Status { get; set; } = "DRAFT";      // document_status_enum
        public string Visibility { get; set; } = "INTERNAL"; // document_visibility_enum
        public Guid? CurrentVersionId { get; set; }
        public Guid? CreatedBy { get; set; }
        public bool IsConfidential { get; set; }
    }

    public sealed class DocumentVersionRow
    {
        public Guid Id { get; init; }
        public Guid TenantId { get; init; }
        public Guid DocumentId { get; init; }

        public int VersionNumber { get; init; }

        public string FileName { get; init; } = "";
        public string FileExtension { get; init; } = "";
        public long FileSizeBytes { get; init; }

        public string StoragePath { get; init; } = "";

        public string? ChecksumMd5 { get; init; }
        public string? ChecksumSha256 { get; init; }

        public string ContentType { get; init; } = "application/octet-stream";

        public Guid? CreatedBy { get; init; }
    }
}
