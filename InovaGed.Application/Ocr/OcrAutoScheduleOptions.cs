namespace InovaGed.Application.Ocr;

public sealed class OcrAutoScheduleOptions
{
    public bool Enabled { get; set; } = true;
    public string RunAt { get; set; } = "18:00";
    public string TimeZone { get; set; } = "America/Belem";
    public Guid TenantId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid? SystemUserId { get; set; }
    public bool OnlyActiveDocuments { get; set; } = true;
    public bool IncludeSubfolders { get; set; } = true;
    public int MaxDocumentsPerRun { get; set; } = 500;
    public int BatchSize { get; set; } = 50;
    public bool SkipIfOcrJobPending { get; set; } = true;
    public bool SkipIfOcrJobProcessing { get; set; } = true;
    public bool SkipIfOcrAvailable { get; set; } = true;
    public bool RetryFailedOcr { get; set; }
    public int RetryFailedOlderThanHours { get; set; } = 24;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"];
}
