using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InovaGed.Application;
using InovaGed.Application.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InovaGed.Application.Common.Preview;

public sealed class LibreOfficePreviewGenerator : IPreviewGenerator
{
    private readonly IFileStorage _storage;
    private readonly ILogger<LibreOfficePreviewGenerator> _logger;
    private readonly string _sofficePath;

    public LibreOfficePreviewGenerator(
        IFileStorage storage,
        IConfiguration cfg,
        ILogger<LibreOfficePreviewGenerator> logger)
    {
        _storage = storage;
        _logger = logger;

        var configured = (cfg["Preview:LibreOfficePath"] ?? "").Trim();
        _sofficePath = !string.IsNullOrWhiteSpace(configured) ? configured : (TryFindLibreOfficePath() ?? "");

        if (string.IsNullOrWhiteSpace(_sofficePath) || !File.Exists(_sofficePath))
        {
            throw new InvalidOperationException(
                "LibreOffice (soffice.exe) não encontrado. Configure Preview:LibreOfficePath no appsettings.json " +
                "(ex.: C:\\Program Files\\LibreOffice\\program\\soffice.exe ou C:\\Program Files (x86)\\LibreOffice\\program\\soffice.exe).");
        }
    }

    public async Task<string> GetOrCreatePreviewPdfAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string sourceStoragePath,
        string originalFileName,
        CancellationToken ct)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "preview";

        // cache: tenant/previews/doc/ver/<basename>.pdf
        var previewRelPath = Path.Combine(
            tenantId.ToString("N"),
            "previews",
            documentId.ToString("N"),
            versionId.ToString("N"),
            baseName + ".pdf"
        ).Replace('\\', '/');

        if (await _storage.ExistsAsync(previewRelPath, ct))
            return previewRelPath;

        var tempRoot = Path.Combine(Path.GetTempPath(), "InovaGedPreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var localInput = Path.Combine(tempRoot, Path.GetFileName(originalFileName));
        var outDir = tempRoot;

        try
        {
            // baixa do storage
            await using (var src = await _storage.OpenReadAsync(sourceStoragePath, ct))
            await using (var dst = new FileStream(localInput, FileMode.Create, FileAccess.Write, FileShare.None))
                await src.CopyToAsync(dst, ct);

            // converte para PDF
            var psi = new ProcessStartInfo
            {
                FileName = _sofficePath,
                Arguments = $"--headless --nologo --nofirststartwizard --convert-to pdf --outdir \"{outDir}\" \"{localInput}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = outDir
            };

            using var p = Process.Start(psi);
            if (p is null)
                throw new InvalidOperationException("Falha ao iniciar o processo do LibreOffice.");

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            await p.WaitForExitAsync(ct);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (p.ExitCode != 0)
            {
                _logger.LogError("LibreOffice falhou. ExitCode={ExitCode}. Err={Err}. Out={Out}",
                    p.ExitCode, stderr, stdout);

                throw new Exception("Falha ao converter arquivo para PDF.");
            }

            // LibreOffice gera PDF com o nome do arquivo (sem extensão)
            var producedPdf = Path.Combine(outDir, Path.GetFileNameWithoutExtension(localInput) + ".pdf");
            if (!File.Exists(producedPdf))
                throw new FileNotFoundException("PDF não foi gerado pelo LibreOffice.", producedPdf);

            // salva derivado (cache)
            await using var pdfStream = new FileStream(producedPdf, FileMode.Open, FileAccess.Read, FileShare.Read);
            await _storage.SaveDerivedAsync(previewRelPath, pdfStream, "application/pdf", ct);

            return previewRelPath;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* ignore */ }
        }
    }

    private static string? TryFindLibreOfficePath()
    {
        var candidates = new[]
        {
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
        };

        foreach (var c in candidates)
            if (File.Exists(c))
                return c;

        return null;
    }
}
