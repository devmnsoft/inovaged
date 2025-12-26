using System.Diagnostics;
using System.Text;
using InovaGed.Application;
using InovaGed.Application.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrMyPdfOcrService : IOcrService
{
    private readonly IFileStorage _storage;
    private readonly ILogger<OcrMyPdfOcrService> _logger;

    private readonly string _ocrmypdfPath;
    private readonly string _popplerBin;
    private readonly string _langs;

    public OcrMyPdfOcrService(
        IFileStorage storage,
        IConfiguration cfg,
        ILogger<OcrMyPdfOcrService> logger)
    {
        _storage = storage;
        _logger = logger;

        _ocrmypdfPath = (cfg["Ocr:OcrMyPdfPath"] ?? "").Trim();
        _popplerBin = (cfg["Ocr:PopplerBinPath"] ?? "").Trim();
        _langs = (cfg["Ocr:Languages"] ?? "por+eng").Trim();
    }

    public async Task<OcrPdfResult> OcrizePdfAsync(
        string pdfStoragePath,
        bool invalidateDigitalSignatures,
        CancellationToken ct)
    {
        ValidateConfig();

        var tempDir = Path.Combine(Path.GetTempPath(), "InovaGedOcr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var inPdf = Path.Combine(tempDir, "input.pdf");
        var outPdf = Path.Combine(tempDir, "output_ocr.pdf");

        try
        {
            // 1) baixa PDF do storage
            _logger.LogInformation("OCR: baixando PDF do storage. Path={Path}", pdfStoragePath);

            await using (var src = await _storage.OpenReadAsync(pdfStoragePath, ct))
            await using (var dst = File.Create(inPdf))
                await src.CopyToAsync(dst, ct);

            // 2) monta args do ocrmypdf
            // IMPORTANTÍSSIMO:
            // - NÃO usar --tesseract-path (tua instalação não aceita, já vimos isso)
            // - PDF assinado: ocrmypdf bloqueia, a menos que use --invalidate-digital-signatures
            var args = new StringBuilder();
            args.Append("--skip-text ");
            args.Append($"-l {_langs} ");

            if (invalidateDigitalSignatures)
                args.Append("--invalidate-digital-signatures ");

            args.Append("--tesseract-timeout 180 ");
            args.Append($"\"{inPdf}\" \"{outPdf}\"");

            _logger.LogInformation("OCR: executando ocrmypdf. Exe={Exe} Args={Args}",
                _ocrmypdfPath, args.ToString());

            var (exit, stdout, stderr) = await RunProcessAsync(
                fileName: _ocrmypdfPath,
                arguments: args.ToString(),
                workingDir: tempDir,
                timeout: TimeSpan.FromMinutes(6),
                ct: ct);

            if (exit != 0)
            {
                // ✅ caso especial: assinatura digital
                // No teu log apareceu exatamente DigitalSignatureError. :contentReference[oaicite:1]{index=1}
                if (!invalidateDigitalSignatures &&
                    !string.IsNullOrWhiteSpace(stderr) &&
                    stderr.Contains("DigitalSignatureError", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("OCR bloqueado por assinatura digital (sem autorização para invalidar).");
                    throw new PdfHasDigitalSignatureException();
                }

                _logger.LogError("ocrmypdf falhou. Exit={ExitCode}. STDERR={Err}. STDOUT={Out}",
                    exit, stderr, stdout);

                throw new Exception("Falha ao executar OCR no PDF (ocrmypdf).");
            }

            if (!File.Exists(outPdf))
                throw new FileNotFoundException("ocrmypdf não gerou o PDF de saída.", outPdf);

            // 3) extrair texto (opcional) via pdftotext
            var text = await ExtractTextWithPdfToTextAsync(outPdf, tempDir, ct);

            // 4) retornar bytes do PDF OCR
            var bytes = await File.ReadAllBytesAsync(outPdf, ct);

            _logger.LogInformation("OCR concluído. PdfBytes={Bytes} TextLen={Len}",
                bytes.Length, text?.Length ?? 0);

            return new OcrPdfResult(bytes, text ?? "");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    private void ValidateConfig()
    {
        if (string.IsNullOrWhiteSpace(_ocrmypdfPath) || !File.Exists(_ocrmypdfPath))
            throw new InvalidOperationException("Ocr:OcrMyPdfPath não configurado ou arquivo não encontrado.");

        if (!string.IsNullOrWhiteSpace(_popplerBin) && !Directory.Exists(_popplerBin))
            throw new InvalidOperationException("Ocr:PopplerBinPath configurado, mas diretório não encontrado.");
    }

    private async Task<string?> ExtractTextWithPdfToTextAsync(string pdfPath, string tempDir, CancellationToken ct)
    {
        var pdftotext = !string.IsNullOrWhiteSpace(_popplerBin)
            ? Path.Combine(_popplerBin, "pdftotext.exe")
            : "";

        if (string.IsNullOrWhiteSpace(pdftotext) || !File.Exists(pdftotext))
        {
            _logger.LogWarning("OCR: pdftotext.exe não encontrado. PopplerBinPath={PopplerBin}", _popplerBin);
            return "";
        }

        var outTxt = Path.Combine(tempDir, "out.txt");

        var psi2 = new ProcessStartInfo
        {
            FileName = pdftotext,
            Arguments = $"\"{pdfPath}\" \"{outTxt}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = tempDir
        };

        using var p2 = Process.Start(psi2)!;
        var stdout = await p2.StandardOutput.ReadToEndAsync();
        var stderr = await p2.StandardError.ReadToEndAsync();
        await p2.WaitForExitAsync(ct);

        if (p2.ExitCode != 0)
        {
            _logger.LogWarning("pdftotext falhou. Exit={ExitCode}. STDERR={Err}. STDOUT={Out}",
                p2.ExitCode, stderr, stdout);
            return "";
        }

        if (!File.Exists(outTxt))
            return "";

        return await File.ReadAllTextAsync(outTxt, Encoding.UTF8, ct);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDir,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Falha ao iniciar processo: {fileName}");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await p.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"Processo excedeu timeout: {fileName}");
        }

        return (p.ExitCode, await stdoutTask, await stderrTask);
    }
}
