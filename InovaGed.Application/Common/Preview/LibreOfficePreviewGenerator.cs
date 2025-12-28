using System.Diagnostics;
using InovaGed.Application;
using InovaGed.Application.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Preview;

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
        _sofficePath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : TryFindLibreOfficePath()
              ?? throw new InvalidOperationException(
                  "LibreOffice (soffice.exe) não encontrado. Configure Preview:LibreOfficePath no appsettings.json.");

        if (!File.Exists(_sofficePath))
            throw new FileNotFoundException("LibreOffice (soffice.exe) não encontrado no caminho informado.", _sofficePath);
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

        var previewRelPath = Path.Combine(
            tenantId.ToString("N"),
            "previews",
            documentId.ToString("N"),
            versionId.ToString("N"),
            baseName + ".pdf"
        ).Replace('\\', '/');

        // cache
        if (await _storage.ExistsAsync(previewRelPath, ct))
            return previewRelPath;

        var tempRoot = Path.Combine(Path.GetTempPath(), "InovaGedPreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var localInput = Path.Combine(tempRoot, Path.GetFileName(originalFileName));

        try
        {
            // 1) baixa do storage
            await using (var src = await _storage.OpenReadAsync(sourceStoragePath, ct))
            await using (var dst = new FileStream(localInput, FileMode.Create, FileAccess.Write, FileShare.None))
                await src.CopyToAsync(dst, ct);

            // 2) executa conversão headless
            var psi = new ProcessStartInfo
            {
                FileName = _sofficePath,
                Arguments =
                    $"--headless --nologo --nolockcheck --nodefault --nofirststartwizard " +
                    $"--convert-to pdf --outdir \"{tempRoot}\" \"{localInput}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = tempRoot
            };

            using var process = Process.Start(psi);
            if (process is null)
                throw new InvalidOperationException("Falha ao iniciar LibreOffice.");

            // 3) timeout real (30s)
            var exited = await Task.Run(() => process.WaitForExit(30000), ct);
            if (!exited)
            {
                try { process.Kill(true); } catch { }
                throw new TimeoutException("LibreOffice excedeu o tempo limite.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("LibreOffice falhou. ExitCode={ExitCode}. Err={Err}. Out={Out}",
                    process.ExitCode, stderr, stdout);
                throw new Exception("Falha ao converter arquivo para PDF.");
            }

            // 4) pega o PDF mais recente gerado (LibreOffice nem sempre respeita nome)
            var pdf = Directory
                .GetFiles(tempRoot, "*.pdf", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (pdf is null || !File.Exists(pdf))
                throw new FileNotFoundException("LibreOffice não gerou PDF.");

            // 5) salva derivado
            await using var pdfStream = new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.Read);
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
