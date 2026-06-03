namespace InovaGed.Application.Ged.Documents;

public sealed class DocumentUploadOptions
{
    public int MaxFileSizeMb { get; set; } = 2048;
    public int MaxBatchFiles { get; set; } = 1000;
    public int ChunkSizeMb { get; set; } = 10;
    public int ChunkedThresholdMb { get; set; } = 50;
    public bool UseChunkedUpload { get; set; } = true;
    public int MaxConcurrentUploadsGlobal { get; set; } = 8;
    public int MaxConcurrentUploadsPerUser { get; set; } = 2;
    public int MaxConcurrentUploadsPerBatch { get; set; } = 2;
    public int UploadTimeoutSeconds { get; set; } = 1800;
    public bool AllowLegacyUploadFallback { get; set; } = false;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt"];
}
