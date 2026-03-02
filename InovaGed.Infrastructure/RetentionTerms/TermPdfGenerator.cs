using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.RetentionTerms;

public interface ITermPdfGenerator
{
    Task<byte[]> RenderPdfFromHtmlAsync(string html, CancellationToken ct);
}

public sealed class LibreOfficeTermPdfGenerator : ITermPdfGenerator
{
    private readonly ILogger<LibreOfficeTermPdfGenerator> _logger;
    private readonly string _sofficePath;

    public LibreOfficeTermPdfGenerator(IConfiguration cfg, ILogger<LibreOfficeTermPdfGenerator> logger)
    {
        _logger = logger;
        _sofficePath = cfg["Preview:LibreOfficePath"] ?? "";
        if (string.IsNullOrWhiteSpace(_sofficePath))
            _logger.LogWarning("LibreOfficePath not configured (Preview:LibreOfficePath).");
    }

    public async Task<byte[]> RenderPdfFromHtmlAsync(string html, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_sofficePath))
            throw new InvalidOperationException("LibreOfficePath não configurado.");

        var work = Path.Combine(Path.GetTempPath(), "inovaged_terms", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        var htmlPath = Path.Combine(work, "term.html");
        var outDir = work;

        await File.WriteAllTextAsync(htmlPath, html, ct);

        var psi = new ProcessStartInfo
        {
            FileName = _sofficePath,
            Arguments = $"--headless --nologo --nofirststartwizard --convert-to pdf --outdir \"{outDir}\" \"{htmlPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(ct);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"LibreOffice falhou. Exit={p.ExitCode} Err={stderr}");

        var pdfPath = Path.Combine(outDir, "term.pdf");
        if (!File.Exists(pdfPath))
        {
            // LibreOffice pode gerar "term.pdf" ou "term.html.pdf" dependendo da versão
            var any = Directory.GetFiles(outDir, "*.pdf").FirstOrDefault();
            if (any is null) throw new InvalidOperationException("PDF não foi gerado.");
            pdfPath = any;
        }

        var bytes = await File.ReadAllBytesAsync(pdfPath, ct);

        try { Directory.Delete(work, true); } catch { /* ignore */ }

        return bytes;
    }
}