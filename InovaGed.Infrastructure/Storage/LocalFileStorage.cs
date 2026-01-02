using System.Security.Cryptography;
using InovaGed.Application.Common.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Storage;

public sealed class LocalStorageOptions
{
    public string RootPath { get; set; } = "App_Data/GedStorage";
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly LocalStorageOptions _opt;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(
        IOptions<LocalStorageOptions> opt,
        ILogger<LocalFileStorage> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    // =========================================================
    // SALVAR ARQUIVO ORIGINAL
    // =========================================================
    public async Task<(string storagePath, long sizeBytes, string md5, string sha256)> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken ct)
    {
        var safeName = SanitizeFileName(originalFileName);

        // tenant/document/version/arquivo.ext
        var relativePath = Path.Combine(
            tenantId.ToString("N"),
            documentId.ToString("N"),
            versionId.ToString("N"),
            safeName);

        var fullPath = Path.Combine(_opt.RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            var (total, md5Hex, shaHex) =
                await WriteWithHashesAsync(fullPath, content, ct);

            return (
                relativePath.Replace('\\', '/'),
                total,
                md5Hex,
                shaHex
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao salvar arquivo no storage local. Path={Path}",
                fullPath);

            try { if (File.Exists(fullPath)) File.Delete(fullPath); }
            catch { /* ignore */ }

            throw;
        }
    }

    // =========================================================
    // SALVAR ARQUIVO DERIVADO (preview, OCR, thumbnails)
    // =========================================================
    public async Task<(long sizeBytes, string md5, string sha256)> SaveDerivedAsync(
        string storagePath,
        Stream content,
        string contentType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("storagePath inválido.", nameof(storagePath));

        var fullPath = Path.Combine(
            _opt.RootPath,
            storagePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            return await WriteWithHashesAsync(fullPath, content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao salvar arquivo derivado. Path={Path}",
                fullPath);

            try { if (File.Exists(fullPath)) File.Delete(fullPath); }
            catch { /* ignore */ }

            throw;
        }
    }

    // =========================================================
    // ABRIR PARA LEITURA
    // =========================================================
    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(
            _opt.RootPath,
            storagePath.Replace('/', Path.DirectorySeparatorChar));

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite, // importante no Windows
            bufferSize: 1024 * 256,
            useAsync: true);

        return Task.FromResult(stream);
    }

    // =========================================================
    // EXISTE?
    // =========================================================
    public Task<bool> ExistsAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(
            _opt.RootPath,
            storagePath.Replace('/', Path.DirectorySeparatorChar));

        return Task.FromResult(File.Exists(fullPath));
    }

    // =========================================================
    // DELETE FORÇADO (LOGADO)
    // =========================================================
    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return Task.CompletedTask;

        var fullPath = NormalizePath(storagePath);

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation(
                    "Arquivo deletado do storage: {Path}",
                    fullPath);
            }
            else
            {
                _logger.LogWarning(
                    "DeleteAsync: arquivo não encontrado. Path={Path}",
                    fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao deletar arquivo do storage. Path={Path}",
                fullPath);
        }

        return Task.CompletedTask;
    }

    // =========================================================
    // DELETE SILENCIOSO (USADO EM CASCADE)
    // =========================================================
    public Task DeleteIfExistsAsync(string storagePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return Task.CompletedTask;

        var fullPath = NormalizePath(storagePath);

        try
        {
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch
        {
            // intencionalmente ignorado
        }

        return Task.CompletedTask;
    }

    // =========================================================
    // HELPERS
    // =========================================================
    private static string NormalizePath(string storagePath)
    {
        var p = storagePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        return Path.IsPathRooted(p)
            ? p
            : Path.Combine("App_Data/GedStorage", p);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return string.IsNullOrWhiteSpace(name)
            ? "file.bin"
            : name.Trim();
    }

    private async Task<(long totalBytes, string md5Hex, string sha256Hex)> WriteWithHashesAsync(
        string fullPath,
        Stream content,
        CancellationToken ct)
    {
        var tmpPath = fullPath + ".tmp";

        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
        catch { /* ignore */ }

        long total = 0;

        using var md5 = MD5.Create();
        using var sha = SHA256.Create();

        await using (var fs = new FileStream(
            tmpPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1024 * 64,
            useAsync: true))
        {
            var buffer = new byte[1024 * 64];
            int read;

            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                md5.TransformBlock(buffer, 0, read, null, 0);
                sha.TransformBlock(buffer, 0, read, null, 0);
                total += read;
            }

            md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        }

        const int attempts = 6;
        for (int i = 1; i <= attempts; i++)
        {
            try
            {
                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                File.Move(tmpPath, fullPath);
                break;
            }
            catch when (i < attempts)
            {
                await Task.Delay(120 * i, ct);
            }
        }

        if (File.Exists(tmpPath))
        {
            try { File.Delete(tmpPath); } catch { }
            throw new IOException("Falha ao gravar arquivo no storage.");
        }

        return (
            total,
            Convert.ToHexString(md5.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha.Hash!).ToLowerInvariant()
        );
    }
}
