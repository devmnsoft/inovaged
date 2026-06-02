namespace InovaGed.Application.Ged.Documents;

public sealed class DocumentUploadOptions
{
    public int MaxFileSizeMb { get; set; } = 100;
    public int MaxBatchFiles { get; set; } = 500;
    public int MaxConcurrentUploadsGlobal { get; set; } = 8;
    public int MaxConcurrentUploadsPerUser { get; set; } = 2;
    public int MaxConcurrentUploadsPerBatch { get; set; } = 2;
    public int UploadTimeoutSeconds { get; set; } = 300;
    public bool AllowLegacyUploadFallback { get; set; } = false;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".doc", ".docx", ".xls", ".xlsx", ".txt"];
}
