using System.Diagnostics;
using System.Text;
using InovaGed.Application.Common.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Application.Common.Preview;

public sealed class LibreOfficePreviewGenerator : IPreviewGenerator
{
    private readonly IFileStorage _storage;
    private readonly ILogger<LibreOfficePreviewGenerator> _logger;
    private readonly LibreOfficeOptions _options;

    public LibreOfficePreviewGenerator(
        IFileStorage storage,
        IOptions<LibreOfficeOptions> options,
        ILogger<LibreOfficePreviewGenerator> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetOrCreatePreviewPdfAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string sourceStoragePath,
        string originalFileName,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId inválido.", nameof(tenantId));

        if (documentId == Guid.Empty)
            throw new ArgumentException("DocumentId inválido.", nameof(documentId));

        if (versionId == Guid.Empty)
            throw new ArgumentException("VersionId inválido.", nameof(versionId));

        if (string.IsNullOrWhiteSpace(sourceStoragePath))
            throw new ArgumentException("StoragePath do arquivo original não informado.", nameof(sourceStoragePath));

        if (string.IsNullOrWhiteSpace(originalFileName))
            originalFileName = "documento";

        if (IsPdf(originalFileName))
            return sourceStoragePath;

        var previewFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}.pdf";

        var previewRelPath = Path.Combine(
            tenantId.ToString("N"),
            "previews",
            documentId.ToString("N"),
            versionId.ToString("N"),
            SanitizeFileName(previewFileName)
        ).Replace('\\', '/');

        if (await _storage.ExistsAsync(previewRelPath, ct))
            return previewRelPath;

        var sofficePath = ResolveSofficePath();

        if (string.IsNullOrWhiteSpace(sofficePath) || !File.Exists(sofficePath))
        {
            throw new InvalidOperationException(
                "LibreOffice não encontrado. Configure Preview:SofficePath ou Preview:LibreOfficePath no appsettings.json. " +
                "Exemplo: C:\\Program Files\\LibreOffice\\program\\soffice.exe");
        }

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "InovaGedPreview",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);

        var safeInputName = SanitizeFileName(originalFileName);
        var localInput = Path.Combine(tempRoot, safeInputName);

        try
        {
            await using (var source = await _storage.OpenReadAsync(sourceStoragePath, ct))
            await using (var target = new FileStream(
                localInput,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                useAsync: true))
            {
                await source.CopyToAsync(target, ct);
            }

            var args =
                $"--headless --nologo --nofirststartwizard --nolockcheck " +
                $"--convert-to pdf --outdir \"{tempRoot}\" \"{localInput}\"";

            var result = await RunProcessAsync(
                fileName: sofficePath,
                arguments: args,
                workingDirectory: tempRoot,
                timeout: ResolveTimeout(),
                ct: ct);

            if (result.ExitCode != 0)
            {
                _logger.LogError(
                    "LibreOffice falhou. File={File}, ExitCode={ExitCode}, StdOut={StdOut}, StdErr={StdErr}",
                    originalFileName,
                    result.ExitCode,
                    result.StdOut,
                    result.StdErr);

                throw new InvalidOperationException(
                    "Falha ao converter arquivo para PDF. Verifique se o LibreOffice está instalado e se o arquivo não está corrompido.");
            }

            var expectedPdf = Path.Combine(
                tempRoot,
                $"{Path.GetFileNameWithoutExtension(safeInputName)}.pdf");

            if (!File.Exists(expectedPdf))
            {
                expectedPdf = Directory
                    .GetFiles(tempRoot, "*.pdf", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault() ?? "";
            }

            if (string.IsNullOrWhiteSpace(expectedPdf) || !File.Exists(expectedPdf))
            {
                _logger.LogError(
                    "LibreOffice não gerou PDF. File={File}, StdOut={StdOut}, StdErr={StdErr}",
                    originalFileName,
                    result.StdOut,
                    result.StdErr);

                throw new FileNotFoundException("O PDF de preview não foi gerado pelo LibreOffice.");
            }

            await using var pdfStream = new FileStream(
                expectedPdf,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            await _storage.SaveDerivedAsync(
                previewRelPath,
                pdfStream,
                "application/pdf",
                ct);

            _logger.LogInformation(
                "Preview PDF gerado. DocumentId={DocumentId}, VersionId={VersionId}, PreviewPath={PreviewPath}",
                documentId,
                versionId,
                previewRelPath);

            return previewRelPath;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // não interrompe por falha de limpeza
            }
        }
    }

    private string ResolveSofficePath()
    {
        var path = _options.GetResolvedSofficePath();

        if (!string.IsNullOrWhiteSpace(path))
            return path;

        var candidates = new[]
        {
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
        };

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    private TimeSpan ResolveTimeout()
    {
        return _options.GetTimeout();
    }

    private static bool IsPdf(string fileName)
    {
        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return string.IsNullOrWhiteSpace(name)
            ? "documento"
            : name.Trim();
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Falha ao iniciar processo: {fileName}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException($"Tempo excedido ao executar: {fileName}");
        }

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StdOut,
        string StdErr);
}