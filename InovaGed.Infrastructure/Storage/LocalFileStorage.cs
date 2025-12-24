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

    public LocalFileStorage(IOptions<LocalStorageOptions> opt, ILogger<LocalFileStorage> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

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

        // ✅ tenant/doc/version/arquivo.ext
        var relative = Path.Combine(
            tenantId.ToString("N"),
            documentId.ToString("N"),
            versionId.ToString("N"),
            safeName);

        var fullPath = Path.Combine(_opt.RootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            var (total, md5Hex, shaHex) = await WriteWithHashesAsync(fullPath, content, ct);

            // padroniza como "path web"
            var storagePath = relative.Replace('\\', '/');
            return (storagePath, total, md5Hex, shaHex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao salvar arquivo no storage local. Path={Path}", fullPath);
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { /* ignore */ }
            throw;
        }
    }

    // ✅ NOVO: salva em um path fixo (ex: previews/...)
    public async Task<(long sizeBytes, string md5, string sha256)> SaveDerivedAsync(
        string storagePath,
        Stream content,
        string contentType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("storagePath inválido.", nameof(storagePath));

        var fullPath = Path.Combine(_opt.RootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            var (total, md5Hex, shaHex) = await WriteWithHashesAsync(fullPath, content, ct);
            return (total, md5Hex, shaHex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao salvar arquivo derivado no storage local. Path={Path}", fullPath);
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { /* ignore */ }
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_opt.RootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 256, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_opt.RootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_opt.RootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private static async Task<(long total, string md5Hex, string shaHex)> WriteWithHashesAsync(
        string fullPath,
        Stream content,
        CancellationToken ct)
    {
        using var md5 = MD5.Create();
        using var sha = SHA256.Create();

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1024 * 1024, useAsync: true);

        var buffer = new byte[1024 * 128];
        int read;
        long total = 0;

        while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);

            md5.TransformBlock(buffer, 0, read, null, 0);
            sha.TransformBlock(buffer, 0, read, null, 0);

            total += read;
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var md5Hex = Convert.ToHexString(md5.Hash!).ToLowerInvariant();
        var shaHex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();

        return (total, md5Hex, shaHex);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "file.bin" : name.Trim();
    }
}
