namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrOptions
{
    public bool Enabled { get; set; } = true;

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
    public int EnvironmentProbeTimeoutSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 3;
    public int WorkerDelaySeconds { get; set; } = 5;
    public int MaxParallelJobs { get; set; } = 1;
    public int LeaseSeconds { get; set; } = 600;
    public int MaxJobMinutes { get; set; } = 30;
    public int PreviewStatusPollingSeconds { get; set; } = 5;

    public int[] RetryBackoffSeconds { get; set; } = new[] { 30, 120, 300 };

    public string PdfMode { get; set; } = "skip-text";
    public bool UseDeskew { get; set; } = true;
    public bool UseClean { get; set; } = false;
    public bool UseRotatePages { get; set; } = true;
    public int OptimizeLevel { get; set; } = 0;
    public string OutputType { get; set; } = "pdf";

    public TimeSpan RetryDelay(int attempt)
    {
        var delays = RetryBackoffSeconds is { Length: > 0 } ? RetryBackoffSeconds : new[] { 30 };
        var index = Math.Max(0, attempt - 1);
        var seconds = index < delays.Length ? delays[index]
            : delays[^1] * Math.Pow(2, Math.Min(16, index - delays.Length + 1));
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 86400));
    }
}
