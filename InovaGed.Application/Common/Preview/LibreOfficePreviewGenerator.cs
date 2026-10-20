using InovaGed.Application.Common.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Application.Common.Preview;

public sealed class LibreOfficePreviewGenerator : IPreviewGenerator
{
    private static readonly SemaphoreSlim ConcurrencySemaphore = new(4, 4);
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

        var configuredPath = ResolveSofficePath();
        var sofficePath = LibreOfficeCommand.ResolveExecutable(configuredPath, File.Exists);

        if (string.IsNullOrWhiteSpace(configuredPath) || !File.Exists(configuredPath) || string.IsNullOrWhiteSpace(sofficePath) || !File.Exists(sofficePath))
        {
            throw new DocumentConversionException(
                "CONVERSION_UNAVAILABLE",
                DocumentConversionException.UnavailableMessage,
                true,
                "LibreOffice não encontrado na configuração Preview:SofficePath ou Preview:LibreOfficePath.");
        }

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "InovaGedPreview",
            Guid.NewGuid().ToString("N"));

        var profileRoot = Path.Combine(tempRoot, "profile");
        var safeInputName = SanitizeFileName(originalFileName);
        var localInput = Path.Combine(tempRoot, safeInputName);

        await ConcurrencySemaphore.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(profileRoot);
            EnsureWritable(profileRoot);
            RemoveStaleWorkDirectories(tempRoot);

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

            var profileUri = new Uri(profileRoot).AbsoluteUri;
            var sizeBytes = new FileInfo(localInput).Length;
            var timeout = LibreOfficeCommand.TimeoutFor(_options, sizeBytes);
            var args = LibreOfficeCommand.Arguments(profileUri, tempRoot, localInput, safeInputName);
            var result = await LibreOfficeProcessRunner.RunAsync(sofficePath, args, tempRoot, timeout, ct);

            if (result.Canceled)
                throw new OperationCanceledException(ct);

            var expectedPdf = Path.Combine(
                tempRoot,
                $"{Path.GetFileNameWithoutExtension(safeInputName)}.pdf");

            if (!File.Exists(expectedPdf))
            {
                expectedPdf = Directory
                    .GetFiles(tempRoot, "*.pdf", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault() ?? "";
            }

            var validPdf = !string.IsNullOrWhiteSpace(expectedPdf)
                && File.Exists(expectedPdf)
                && HasPdfHeader(expectedPdf)
                && new FileInfo(expectedPdf).Length >= 32;

            if (!LibreOfficeCommand.AcceptOutput(result, validPdf))
            {
                var failure = DocumentConversionException.FromProcess(result.StdErr, result.ExitCode, result.TimedOut || !result.Exited, result.Started, result.Exited);
                _logger.LogError(
                    "Conversão LibreOffice falhou. Code={Code} Extension={Extension} SizeBytes={SizeBytes} ExitCode={ExitCode} ElapsedMs={ElapsedMs} TimedOut={TimedOut} Exited={Exited} ProcessId={ProcessId}",
                    failure.FailureCode,
                    Path.GetExtension(safeInputName),
                    sizeBytes,
                    result.ExitCode,
                    result.ElapsedMs,
                    result.TimedOut,
                    result.Exited,
                    result.ProcessId);
                throw failure;
            }

            if (result.TimedOut)
            {
                _logger.LogWarning(
                    "PDF válido aceito depois do encerramento da conversão. Extension={Extension} SizeBytes={SizeBytes} ElapsedMs={ElapsedMs} ProcessId={ProcessId}",
                    Path.GetExtension(safeInputName),
                    sizeBytes,
                    result.ElapsedMs,
                    result.ProcessId);
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
                "Preview PDF gerado. DocumentId={DocumentId} VersionId={VersionId} Extension={Extension} SizeBytes={SizeBytes} ElapsedMs={ElapsedMs} ExitCode={ExitCode}",
                documentId,
                versionId,
                Path.GetExtension(safeInputName),
                sizeBytes,
                result.ElapsedMs,
                result.ExitCode);

            return previewRelPath;
        }
        finally
        {
            ConcurrencySemaphore.Release();
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void EnsureWritable(string directory)
    {
        var probe = Path.Combine(directory, ".write-probe");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DocumentConversionException("CONVERSION_PROFILE", DocumentConversionException.UnavailableMessage, true, "O perfil temporário da conversão não é gravável.", ex);
        }
    }

    private void RemoveStaleWorkDirectories(string current)
    {
        var root = Path.Combine(Path.GetTempPath(), "InovaGedPreview");
        if (!Directory.Exists(root))
            return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(directory, current, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddHours(-1))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Diretório temporário antigo de conversão permanece em uso.");
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool HasPdfHeader(string path)
    {
        Span<byte> header = stackalloc byte[5];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.Read(header) == 5 && header[0] == (byte)'%' && header[1] == (byte)'P' && header[2] == (byte)'D' && header[3] == (byte)'F' && header[4] == (byte)'-';
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
}