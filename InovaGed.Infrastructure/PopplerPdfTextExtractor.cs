using System.Diagnostics;
using InovaGed.Application;
using InovaGed.Application.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class PopplerPdfTextExtractor : IPdfTextExtractor
{
    private readonly IFileStorage _storage;
    private readonly ILogger<PopplerPdfTextExtractor> _logger;
    private readonly string _pdfToTextPath;

    public PopplerPdfTextExtractor(
        IFileStorage storage,
        IConfiguration cfg,
        ILogger<PopplerPdfTextExtractor> logger)
    {
        _storage = storage;
        _logger = logger;
        _pdfToTextPath = cfg["Ocr:PdfToTextPath"]
            ?? throw new InvalidOperationException("Ocr:PdfToTextPath não configurado.");
    }

    public async Task<string> ExtractTextAsync(string pdfStoragePath, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "InovaGedOcr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var localPdf = Path.Combine(tempDir, "doc.pdf");
        var localTxt = Path.Combine(tempDir, "doc.txt");

        try
        {
            // baixa PDF
            await using (var src = await _storage.OpenReadAsync(pdfStoragePath, ct))
            await using (var dst = File.Create(localPdf))
                await src.CopyToAsync(dst, ct);

            var psi = new ProcessStartInfo
            {
                FileName = _pdfToTextPath,
                Arguments = $"\"{localPdf}\" \"{localTxt}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync(ct);

            if (!File.Exists(localTxt))
                return string.Empty;

            return await File.ReadAllTextAsync(localTxt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao extrair texto do PDF.");
            return string.Empty;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
