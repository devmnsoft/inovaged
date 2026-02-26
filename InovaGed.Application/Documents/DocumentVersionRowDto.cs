namespace InovaGed.Application.Documents.Workflow
{
    public sealed class DocumentVersionRowDto
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }

        public int VersionNumber { get; set; }            // se existir no banco
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }
        public string StoragePath { get; set; } = "";

        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public bool IsCurrent { get; set; }

        // ✅ OCR
        public string? OcrStatus { get; set; }
        public long? OcrJobId { get; set; }
        public string? OcrErrorMessage { get; set; }
        public bool IsOcrVersion { get; set; }            // se você tiver a coluna; senão, deixe e calcule
        public DateTime? OcrRequestedAt { get; set; }
        public DateTime? OcrStartedAt { get; set; }
        public DateTime? OcrFinishedAt { get; set; }
        public bool OcrInvalidateDigitalSignatures { get; set; }
    }
}
