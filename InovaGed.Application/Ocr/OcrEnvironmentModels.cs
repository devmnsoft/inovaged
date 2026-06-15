using System.Text.Json.Serialization;

namespace InovaGed.Application.Ocr;

public interface IOcrEnvironmentValidator
{
    Task<OcrEnvironmentReport> ValidateAsync(CancellationToken ct);
}

public sealed record OcrEnvironmentReport(
    bool IsValid,
    string ProcessUser,
    string WindowsIdentity,
    string StoragePath,
    IReadOnlyList<OcrEnvironmentCheck> Checks,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAtUtc);

public sealed record OcrEnvironmentCheck(
    string Name,
    string ConfiguredPath,
    bool Exists,
    bool PermissionOk,
    string? Version,
    string? TestCommand,
    OcrCommandResult? CommandResult,
    string? ErrorMessage);

public sealed record OcrCommandResult(
    int? ExitCode,
    string StdOut,
    string StdErr,
    long ElapsedMs,
    bool TimedOut);

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
    public bool IsPermanent => Code is OcrFailureCode.OCR_ENVIRONMENT_INVALID
        or OcrFailureCode.OCR_TESSERACT_LANGUAGE_MISSING
        or OcrFailureCode.OCR_GHOSTSCRIPT_NOT_FOUND
        or OcrFailureCode.OCR_QPDF_NOT_FOUND
        or OcrFailureCode.OCR_PDF_ENCRYPTED
        or OcrFailureCode.OCR_PDF_CORRUPTED
        or OcrFailureCode.OCR_INPUT_FILE_NOT_FOUND
        or OcrFailureCode.OCR_INPUT_FILE_NOT_READABLE
        or OcrFailureCode.OCR_OUTPUT_WRITE_DENIED;

    public OcrProcessingException(OcrFailureCode code, string friendlyMessage, string technicalMessage, string? detailsJson = null, Exception? inner = null)
        : base(technicalMessage, inner)
    {
        Code = code;
        FriendlyMessage = friendlyMessage;
        DetailsJson = detailsJson;
    }
}
