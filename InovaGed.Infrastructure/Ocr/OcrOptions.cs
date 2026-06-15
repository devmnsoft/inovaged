namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrOptions
{
    public string? OcrMyPdfPath { get; set; }
    public string? PythonPath { get; set; }
    public string? PdfToTextPath { get; set; }
    public string? GhostscriptBinPath { get; set; }
    public string? TesseractPath { get; set; }
    public string? TesseractDataPath { get; set; }
    public string? PopplerBinPath { get; set; }
    public string? QpdfPath { get; set; }
    public string? QpdfBinPath { get; set; }
    public string Languages { get; set; } = "por+eng";
    public int TimeoutMinutes { get; set; } = 8;
    public int MaxAttempts { get; set; } = 3;
    public int MaxParallelJobs { get; set; } = 1;
    public string PdfMode { get; set; } = "skip-text";
    public bool UseDeskew { get; set; } = true;
    public bool UseClean { get; set; } = false;
    public bool UseRotatePages { get; set; } = true;
    public int OptimizeLevel { get; set; } = 0;
    public string OutputType { get; set; } = "pdf";
}
