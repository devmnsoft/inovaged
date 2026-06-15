using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
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

    public OcrMyPdfOcrService(IFileStorage storage, IOptions<OcrOptions> options, IOcrEnvironmentValidator environmentValidator, ILogger<OcrMyPdfOcrService> logger)
    {
        _storage = storage;
        _logger = logger;
        _environmentValidator = environmentValidator;
        _options = options.Value;
    }

    public async Task<OcrPdfResult> OcrizePdfAsync(string pdfStoragePath, bool invalidateDigitalSignatures, CancellationToken ct)
    {
        var env = await _environmentValidator.ValidateAsync(ct);
        if (!env.IsValid)
            throw BuildException(OcrFailureCode.OCR_ENVIRONMENT_INVALID, "OCR falhou porque o ambiente OCR não está configurado corretamente.", "Ambiente OCR inválido. Verifique /SystemHealth/OcrEnvironment.", env);

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
            var psi = CreateOcrProcessStartInfo(inPdf, outTmp, tempDir, invalidateDigitalSignatures);
            _logger.LogInformation("OCR: executando OCRmyPDF. Exe={Exe} Args={Args} User={User}", psi.FileName, string.Join(' ', psi.ArgumentList), WindowsIdentity.GetCurrent().Name);
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
                stdout = TrimMax(stdout, 4000),
                stderr = TrimMax(stderr, 4000),
                elapsedMs = sw.ElapsedMilliseconds,
                timeoutSeconds = (int)TimeSpan.FromMinutes(Math.Max(1, _options.TimeoutMinutes)).TotalSeconds,
                processUser = WindowsIdentity.GetCurrent().Name,
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
            var text = await ExtractTextWithPdfToTextAsync(outPdf, tempDir, ct);
            var bytes = await File.ReadAllBytesAsync(outPdf, ct);
            return new OcrPdfResult(bytes, text ?? "");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private ProcessStartInfo CreateOcrProcessStartInfo(string inPdf, string outPdf, string tempDir, bool invalidateDigitalSignatures)
    {
        var psi = new ProcessStartInfo { FileName = _options.OcrMyPdfPath ?? "ocrmypdf", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = tempDir, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        psi.ArgumentList.Add("--language"); psi.ArgumentList.Add(string.IsNullOrWhiteSpace(_options.Languages) ? "por+eng" : _options.Languages);
        if (_options.UseDeskew) psi.ArgumentList.Add("--deskew");
        if (_options.UseClean) psi.ArgumentList.Add("--clean");
        if (_options.UseRotatePages) psi.ArgumentList.Add("--rotate-pages");
        psi.ArgumentList.Add(NormalizePdfMode(_options.PdfMode));
        psi.ArgumentList.Add("--optimize"); psi.ArgumentList.Add(Math.Clamp(_options.OptimizeLevel, 0, 3).ToString());
        psi.ArgumentList.Add("--output-type"); psi.ArgumentList.Add(string.IsNullOrWhiteSpace(_options.OutputType) ? "pdf" : _options.OutputType);
        if (invalidateDigitalSignatures) psi.ArgumentList.Add("--invalidate-digital-signatures");
        psi.ArgumentList.Add(inPdf); psi.ArgumentList.Add(outPdf);
        if (!string.IsNullOrWhiteSpace(_options.TesseractDataPath)) psi.Environment["TESSDATA_PREFIX"] = _options.TesseractDataPath;
        psi.Environment["PATH"] = BuildOcrPathEnvironment();
        return psi;
    }

    private string BuildOcrPathEnvironment()
    {
        var parts = new[] { _options.GhostscriptBinPath, Path.GetDirectoryName(_options.TesseractPath ?? ""), _options.PopplerBinPath, _options.QpdfBinPath, Path.GetDirectoryName(_options.PythonPath ?? ""), Path.GetDirectoryName(_options.OcrMyPdfPath ?? "") }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(Path.PathSeparator, parts.Concat(new[] { Environment.GetEnvironmentVariable("PATH") ?? "" }));
    }

    private async Task<string?> ExtractTextWithPdfToTextAsync(string pdfPath, string tempDir, CancellationToken ct)
    {
        var pdftotext = !string.IsNullOrWhiteSpace(_options.PdfToTextPath) ? _options.PdfToTextPath : Path.Combine(_options.PopplerBinPath ?? "", "pdftotext.exe");
        if (string.IsNullOrWhiteSpace(pdftotext) || !File.Exists(pdftotext)) return "";
        var outTxt = Path.Combine(tempDir, "out.txt");
        var psi = new ProcessStartInfo(pdftotext) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true, WorkingDirectory = tempDir, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        psi.ArgumentList.Add(pdfPath); psi.ArgumentList.Add(outTxt); psi.Environment["PATH"] = BuildOcrPathEnvironment();
        var (exit, stdout, stderr, _) = await RunProcessAsync(psi, TimeSpan.FromSeconds(60), ct);
        if (exit != 0) { _logger.LogWarning("pdftotext falhou. Exit={ExitCode}. STDERR={Err}. STDOUT={Out}", exit, TrimMax(stderr, 2000), TrimMax(stdout, 2000)); return ""; }
        return File.Exists(outTxt) ? await File.ReadAllTextAsync(outTxt, Encoding.UTF8, ct) : "";
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr, bool TimedOut)> RunProcessAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken ct)
    {
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Falha ao iniciar processo: {psi.FileName}");
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { try { if (!p.HasExited) p.Kill(true); } catch { } return (-1, await stdoutTask, await stderrTask, true); }
        return (p.ExitCode, await stdoutTask, await stderrTask, false);
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

    private static OcrProcessingException BuildException(OcrFailureCode code, string friendly, string technical, object? details) => new(code, friendly, technical, details is null ? null : JsonSerializer.Serialize(details));
    private static string NormalizePdfMode(string? mode) => (mode ?? "skip-text").Trim().ToLowerInvariant() switch { "force-ocr" => "--force-ocr", "redo-ocr" => "--redo-ocr", _ => "--skip-text" };
    private static string TrimMax(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}
