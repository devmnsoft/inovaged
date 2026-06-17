using System.Text.Json.Serialization;

namespace InovaGed.Application.Ocr;

public interface IOcrEnvironmentValidator
{
    Task<OcrEnvironmentValidationResult> ValidateAsync(CancellationToken ct);
}

public sealed class OcrEnvironmentValidationResult
{
    public bool IsValid { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string ProcessUser { get; set; } = string.Empty;
    public string WindowsIdentity { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string CurrentDirectory { get; set; } = string.Empty;
    public string BaseDirectory { get; set; } = string.Empty;
    public string EffectivePath { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public List<OcrEnvironmentCheckResult> Checks { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OcrEnvironmentCheckResult
{
    public string Name { get; set; } = string.Empty;
    public string? ConfigKey { get; set; }
    public string? Path { get; set; }
    public string ConfiguredPath => Path ?? string.Empty;
    public bool Required { get; set; } = true;
    public bool Exists { get; set; }
    public bool CanExecute { get; set; }
    public bool PermissionOk => Success;
    public bool Success { get; set; }
    public string? VersionCommand { get; set; }
    public string? TestCommand => VersionCommand;
    public int? ExitCode { get; set; }
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage => Message;
    public string? Suggestion { get; set; }
    public long ElapsedMs { get; set; }
    public OcrProcessResult? ProcessResult { get; set; }
    public OcrProcessResult? CommandResult => ProcessResult;
    public string? Version => FirstNonEmptyLine(StdOut, StdErr);
    private static string? FirstNonEmptyLine(params string?[] values) => values.SelectMany(v => (v ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)).Select(v => v.Trim()).FirstOrDefault();
}

public sealed record OcrProcessResult(int? ExitCode, string StdOut, string StdErr, bool TimedOut, long ElapsedMs, string? ExceptionMessage)
{
    public bool Success => ExitCode == 0 && !TimedOut && string.IsNullOrWhiteSpace(ExceptionMessage);
}

public sealed record OcrCommandResult(int? ExitCode, string StdOut, string StdErr, long ElapsedMs, bool TimedOut);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OcrFailureCode
{
    OCR_ENVIRONMENT_INVALID,
    OCR_INPUT_FILE_NOT_FOUND,
    OCR_INPUT_FILE_NOT_READABLE,
    OCR_OUTPUT_WRITE_DENIED,
    OCR_PROCESS_TIMEOUT,
    OCR_PROCESS_EXIT_NON_ZERO,
    OCR_TESSERACT_LANGUAGE_MISSING,
    OCR_GHOSTSCRIPT_NOT_FOUND,
    OCR_QPDF_NOT_FOUND,
    OCR_PDF_ENCRYPTED,
    OCR_PDF_CORRUPTED,
    OCR_CANCELLED,
    OCR_UNKNOWN_ERROR
}

public sealed class OcrProcessingException : Exception
{
    public OcrFailureCode Code { get; }
    public string FriendlyMessage { get; }
    public string? DetailsJson { get; }
    public string? TechnicalDetailsJson => DetailsJson;
    public bool IsPermanent => Code is OcrFailureCode.OCR_ENVIRONMENT_INVALID or OcrFailureCode.OCR_TESSERACT_LANGUAGE_MISSING or OcrFailureCode.OCR_GHOSTSCRIPT_NOT_FOUND or OcrFailureCode.OCR_QPDF_NOT_FOUND or OcrFailureCode.OCR_PDF_ENCRYPTED or OcrFailureCode.OCR_PDF_CORRUPTED or OcrFailureCode.OCR_INPUT_FILE_NOT_FOUND or OcrFailureCode.OCR_INPUT_FILE_NOT_READABLE or OcrFailureCode.OCR_OUTPUT_WRITE_DENIED;

    public OcrProcessingException(OcrFailureCode code, string friendlyMessage, string technicalMessage, string? detailsJson = null, Exception? inner = null)
        : base(technicalMessage, inner)
    {
        Code = code;
        FriendlyMessage = friendlyMessage;
        DetailsJson = detailsJson;
    }
}
