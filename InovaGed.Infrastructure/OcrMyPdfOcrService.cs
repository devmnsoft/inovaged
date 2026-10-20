using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InovaGed.Application;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrMyPdfOcrService : IOcrService
{
    private readonly IFileStorage _storage;
    private readonly ILogger<OcrMyPdfOcrService> _logger;
    private readonly IOcrEnvironmentValidator _environmentValidator;
    private readonly OcrOptions _options;
    private readonly IOcrProcessRunner _runner;

    public OcrMyPdfOcrService(IFileStorage storage, IOptions<OcrOptions> options, IOcrEnvironmentValidator environmentValidator, ILogger<OcrMyPdfOcrService> logger, IOcrProcessRunner? runner = null)
    {
        _storage = storage;
        _logger = logger;
        _environmentValidator = environmentValidator;
        _options = options.Value;
        _runner = runner ?? new OcrProcessRunner();
    }

    public async Task<OcrPdfResult> OcrizePdfAsync(string pdfStoragePath, bool invalidateDigitalSignatures, CancellationToken ct)
    {
        var env = await _environmentValidator.ValidateAsync(ct);
        if (!env.IsValid)
            throw BuildEnvironmentException(env);

        var tempDir = Path.Combine(Path.GetTempPath(), "InovaGedOcr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var inPdf = Path.Combine(tempDir, "input.pdf");
        var outTmp = Path.Combine(tempDir, "output_ocr.tmp.pdf");
        var outPdf = Path.Combine(tempDir, "output_ocr.pdf");

        try
        {
            await using (var src = await _storage.OpenReadAsync(pdfStoragePath, ct))
            await using (var dst = File.Create(inPdf))
                await src.CopyToAsync(dst, ct);

            if (!File.Exists(inPdf))
                throw BuildException(OcrFailureCode.OCR_INPUT_FILE_NOT_FOUND, "OCR falhou porque o arquivo de entrada não foi localizado.", "Arquivo de entrada OCR não encontrado.", null);

            var sw = Stopwatch.StartNew();
            var deadline = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.TimeoutMinutes));
            var psi = CreateOcrProcessStartInfo(inPdf, outTmp, tempDir, invalidateDigitalSignatures);
            _logger.LogInformation("OCR: executando OCRmyPDF. Exe={Exe}", psi.FileName);
            var (exit, stdout, stderr, timedOut) = await RunProcessAsync(psi, TimeSpan.FromMinutes(Math.Max(1, _options.TimeoutMinutes)), ct);
            sw.Stop();

            var details = new
            {
                commandName = "ocrmypdf",
                arguments = psi.ArgumentList.ToArray(),
                inputFilePath = inPdf,
                outputFilePath = outTmp,
                inputFileExists = File.Exists(inPdf),
                inputFileSize = new FileInfo(inPdf).Length,
                workingDirectory = tempDir,
                exitCode = exit,
                elapsedMs = sw.ElapsedMilliseconds,
                timeoutSeconds = (int)TimeSpan.FromMinutes(Math.Max(1, _options.TimeoutMinutes)).TotalSeconds,
                processUser = Environment.UserName,
                correlationId = System.Diagnostics.Activity.Current?.Id
            };

            if (timedOut)
                throw BuildException(OcrFailureCode.OCR_PROCESS_TIMEOUT, "OCR excedeu o tempo limite.", "OCRmyPDF excedeu o tempo limite.", details);

            if (exit != 0)
            {
                if (!invalidateDigitalSignatures && stderr.Contains("DigitalSignatureError", StringComparison.OrdinalIgnoreCase))
                    throw new PdfHasDigitalSignatureException();
                var code = Classify(stderr + "\n" + stdout);
                throw BuildException(code, Friendly(code), $"OCRmyPDF falhou (ExitCode={exit}). Verifique /SystemHealth/OcrEnvironment. Código: {code}.", details);
            }

            if (!File.Exists(outTmp))
                throw BuildException(OcrFailureCode.OCR_OUTPUT_WRITE_DENIED, "OCR falhou porque o PDF de saída não foi gerado.", "OCRmyPDF concluiu sem gerar saída.", details);

            File.Move(outTmp, outPdf, overwrite: true);
            string? recoveryMethod = null;
            var text = await ExtractTextWithPdfToTextAsync(outPdf, tempDir, ct);
            if (OcrTextQuality.IsFragmented(text!) && _options.UseRotatePages)
            {
                // Sparse or skewed pages can be falsely rotated by OSD. Retry from the intact
                // input once, with deskew retained and cardinal rotation disabled.
                _logger.LogWarning("OCR fragmentado: repetindo leitura local sem rotação automática.");
                var retryPdf = Path.Combine(tempDir, "output_no_rotation.pdf");
                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    throw BuildException(OcrFailureCode.OCR_PROCESS_TIMEOUT, "OCR excedeu o tempo limite.", "Limite total do OCR excedido.", new { stage = "QUALITY_RECOVERY" });
                var retryPsi = CreateOcrProcessStartInfo(inPdf, retryPdf, tempDir, invalidateDigitalSignatures, allowRotate: false);
                var retry = await RunProcessAsync(retryPsi, remaining, ct);
                if (retry.TimedOut)
                    throw BuildException(OcrFailureCode.OCR_PROCESS_TIMEOUT, "OCR excedeu o tempo limite.", "Limite total do OCR excedido.", new { stage = "QUALITY_RECOVERY" });
                if (retry.ExitCode == 0 && File.Exists(retryPdf))
                {
                    var retryText = await ExtractTextWithPdfToTextAsync(retryPdf, tempDir, ct, rawOrder: true);
                    if (!OcrTextQuality.IsFragmented(retryText!))
                    {
                        text = retryText;
                        recoveryMethod = "tesseract-psm6-no-rotation-poppler-raw";
                        File.Move(retryPdf, outPdf, overwrite: true);
                    }
                }
            }
            if (OcrTextQuality.IsFragmented(text!))
                throw BuildException(OcrFailureCode.OCR_TEXT_LOW_QUALITY,
                    "Texto excessivamente fragmentado. Confira a orientação e a qualidade do documento antes de reprocessar.",
                    "OCR requer conferência de qualidade.", new { stage = "TEXT_QUALITY", reviewRequired = true });
            var bytes = await File.ReadAllBytesAsync(outPdf, ct);
            return new OcrPdfResult(bytes, text ?? "", recoveryMethod);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private ProcessStartInfo CreateOcrProcessStartInfo(string inPdf, string outPdf, string tempDir, bool invalidateDigitalSignatures, bool allowRotate = true)
    {
        var psi = new ProcessStartInfo { FileName = _options.OcrMyPdfPath ?? "ocrmypdf", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = tempDir, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        psi.ArgumentList.Add("--language"); psi.ArgumentList.Add(string.IsNullOrWhiteSpace(_options.Languages) ? "por+eng" : _options.Languages);
        if (_options.UseDeskew) psi.ArgumentList.Add("--deskew");
        if (_options.UseClean) psi.ArgumentList.Add("--clean");
        if (_options.UseRotatePages && allowRotate) psi.ArgumentList.Add("--rotate-pages");
        if (!allowRotate)
        {
            // Recovery targets fragmented layout/orientation. The normal pass keeps
            // automatic segmentation for documents with multiple columns.
            psi.ArgumentList.Add("--tesseract-pagesegmode"); psi.ArgumentList.Add("6");
        }
        psi.ArgumentList.Add(NormalizePdfMode(_options.PdfMode));
        psi.ArgumentList.Add("--optimize"); psi.ArgumentList.Add(Math.Clamp(_options.OptimizeLevel, 0, 3).ToString());
        psi.ArgumentList.Add("--output-type"); psi.ArgumentList.Add(string.IsNullOrWhiteSpace(_options.OutputType) ? "pdf" : _options.OutputType);
        if (invalidateDigitalSignatures) psi.ArgumentList.Add("--invalidate-digital-signatures");
        psi.ArgumentList.Add(inPdf); psi.ArgumentList.Add(outPdf);
        if (!string.IsNullOrWhiteSpace(_options.TesseractDataPath)) psi.Environment["TESSDATA_PREFIX"] = _options.TesseractDataPath;
        psi.Environment["PATH"] = BuildOcrPathEnvironment(_options);
        return psi;
    }

    private static string BuildOcrPathEnvironment(OcrOptions options) => OcrEnvironmentValidator.BuildOcrPathEnvironment(options);

    private async Task<string?> ExtractTextWithPdfToTextAsync(string pdfPath, string tempDir, CancellationToken ct, bool rawOrder = false)
    {
        var executable = OperatingSystem.IsWindows() ? "pdftotext.exe" : "pdftotext";
        var pdftotext = !string.IsNullOrWhiteSpace(_options.PdfToTextPath) ? _options.PdfToTextPath
            : string.IsNullOrWhiteSpace(_options.PopplerBinPath) ? executable : Path.Combine(_options.PopplerBinPath, executable);
        var outTxt = Path.Combine(tempDir, "out.txt");
        var psi = new ProcessStartInfo(pdftotext) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true, WorkingDirectory = tempDir, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        psi.ArgumentList.Add("-enc"); psi.ArgumentList.Add("UTF-8");
        if (rawOrder) psi.ArgumentList.Add("-raw");
        psi.ArgumentList.Add(pdfPath); psi.ArgumentList.Add(outTxt); psi.Environment["PATH"] = BuildOcrPathEnvironment(_options);
        var (exit, _, _, timedOut) = await RunProcessAsync(psi, TimeSpan.FromSeconds(60), ct);
        if (exit != 0 || timedOut || !File.Exists(outTxt))
            throw BuildException(OcrFailureCode.OCR_TEXT_EXTRACTION_FAILED,
                "O PDF foi processado, mas a extração de texto falhou. Reprocesse após verificar o Poppler.",
                "Falha na extração de texto pesquisável.", new { stage = "TEXT_EXTRACTION", exitCode = exit, timedOut });
        var text = await File.ReadAllTextAsync(outTxt, Encoding.UTF8, ct);
        if (string.IsNullOrWhiteSpace(text))
            throw BuildException(OcrFailureCode.OCR_TEXT_EMPTY,
                "Nenhum texto legível foi extraído. O documento precisa de conferência ou de uma imagem de melhor qualidade.",
                "OCR sem texto pesquisável.", new { stage = "TEXT_EXTRACTION" });
        return text;
    }

    private async Task<(int ExitCode, string StdOut, string StdErr, bool TimedOut)> RunProcessAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken ct)
    {
        var result = await _runner.RunVersionAsync(psi.FileName, psi.ArgumentList.ToArray(), psi.WorkingDirectory,
            psi.Environment.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value!), timeout, ct);
        return (result.ExitCode ?? -1, result.StdOut, result.StdErr, result.TimedOut);
    }

    private static OcrFailureCode Classify(string s)
    {
        if (s.Contains("traineddata", StringComparison.OrdinalIgnoreCase) || s.Contains("tesseract", StringComparison.OrdinalIgnoreCase)) return OcrFailureCode.OCR_TESSERACT_LANGUAGE_MISSING;
        if (s.Contains("ghostscript", StringComparison.OrdinalIgnoreCase) || s.Contains("gswin", StringComparison.OrdinalIgnoreCase)) return OcrFailureCode.OCR_GHOSTSCRIPT_NOT_FOUND;
        if (s.Contains("qpdf", StringComparison.OrdinalIgnoreCase)) return OcrFailureCode.OCR_QPDF_NOT_FOUND;
        if (s.Contains("encrypted", StringComparison.OrdinalIgnoreCase) || s.Contains("password", StringComparison.OrdinalIgnoreCase)) return OcrFailureCode.OCR_PDF_ENCRYPTED;
        if (s.Contains("invalid pdf", StringComparison.OrdinalIgnoreCase) || s.Contains("corrupt", StringComparison.OrdinalIgnoreCase)) return OcrFailureCode.OCR_PDF_CORRUPTED;
        if (s.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)) return OcrFailureCode.OCR_INPUT_FILE_NOT_FOUND;
        return OcrFailureCode.OCR_PROCESS_EXIT_NON_ZERO;
    }

    private static string Friendly(OcrFailureCode code) => code switch
    {
        OcrFailureCode.OCR_TESSERACT_LANGUAGE_MISSING => "OCR falhou porque o idioma português/inglês do Tesseract não foi localizado.",
        OcrFailureCode.OCR_GHOSTSCRIPT_NOT_FOUND => "OCR falhou porque o Ghostscript não foi localizado.",
        OcrFailureCode.OCR_QPDF_NOT_FOUND => "OCR falhou porque o qpdf não foi localizado.",
        OcrFailureCode.OCR_PDF_ENCRYPTED => "OCR falhou porque o PDF está protegido por senha.",
        OcrFailureCode.OCR_PDF_CORRUPTED => "OCR falhou porque o PDF parece corrompido.",
        _ => $"OCR falhou ao chamar OCRmyPDF. Verifique o ambiente OCR em /SystemHealth/OcrEnvironment. Código: {code}."
    };

    private static OcrProcessingException BuildEnvironmentException(OcrEnvironmentValidationResult env)
    {
        var failed = env.Checks.Where(c => c.Required && !c.Success).ToList();
        var reasons = failed.Select(c => $"{c.Name} {(c.CanExecute ? "" : "não executa ")}{c.Message ?? c.Suggestion ?? "inválido"}".Trim()).ToList();
        var summary = reasons.Count == 0 ? env.Summary : "Ambiente OCR inválido: " + string.Join("; ", reasons) + ". Verifique /SystemHealth/OcrEnvironment.";
        var details = new { code = OcrFailureCode.OCR_ENVIRONMENT_INVALID.ToString(), summary, processUser = env.ProcessUser, windowsIdentity = env.WindowsIdentity, machineName = env.MachineName, checks = failed };
        return new OcrProcessingException(OcrFailureCode.OCR_ENVIRONMENT_INVALID, summary, summary, JsonSerializer.Serialize(details));
    }

    private static OcrProcessingException BuildException(OcrFailureCode code, string friendly, string technical, object? details)
    {
        var payload = new OcrFailurePayload
        {
            Code = code.ToString(),
            Details = details
        };

        return new(code, friendly, technical, JsonSerializer.Serialize(payload));
    }

    private sealed class OcrFailurePayload
    {
        [JsonPropertyName("code")]
        public string Code { get; init; } = string.Empty;

        [JsonPropertyName("details")]
        public object? Details { get; init; }
    }

    private static string NormalizePdfMode(string? mode) => (mode ?? "skip-text").Trim().ToLowerInvariant() switch { "force-ocr" => "--force-ocr", "redo-ocr" => "--redo-ocr", _ => "--skip-text" };
    private static string TrimMax(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}
