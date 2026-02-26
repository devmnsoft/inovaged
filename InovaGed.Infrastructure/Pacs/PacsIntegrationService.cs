using System.Security.Cryptography;
using InovaGed.Application.Pacs;
using InovaGed.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Pacs;

public sealed class PacsIntegrationService
{
    private readonly ITicketRepository _repo;
    private readonly IOcrQueue _ocr;
    private readonly ILogger<PacsIntegrationService> _logger;
    private readonly StorageLocalOptions _storage;

    public PacsIntegrationService(
        ITicketRepository repo,
        IOcrQueue ocr,
        IOptions<StorageLocalOptions> storage,
        ILogger<PacsIntegrationService> logger)
    {
        _repo = repo;
        _ocr = ocr;
        _logger = logger;
        _storage = storage.Value;
    }

    public async Task<Guid> CreateTicketAndUploadAsync(
        Guid tenantId,
        string protocolCode,
        string? patientName,
        string? patientId,
        string? modality,
        string? examType,
        string? studyUid,
        string? notes,
        IReadOnlyList<IFormFile> files,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(protocolCode))
            throw new ArgumentException("protocolCode é obrigatório.");

        if (files is null || files.Count == 0)
            throw new ArgumentException("Envie pelo menos 1 imagem.");

        var ticketId = await _repo.CreateTicketAsync(tenantId, protocolCode.Trim(), patientName, patientId, modality, examType, studyUid, notes, ct);

        // Diretório fixo no disco:
        // {Root}\pacs\{tenant}\{ticket}\original
        var root = _storage.RootPath;
        var baseDir = StoragePath.CombineSafe(root, "pacs", tenantId.ToString("N"), ticketId.ToString("N"), "original");
        Directory.CreateDirectory(baseDir);

        foreach (var f in files)
        {
            if (f.Length <= 0) continue;

            var fileId = Guid.NewGuid();
            var safeName = MakeSafeFileName(f.FileName);
            var ext = Path.GetExtension(safeName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

            var diskFileName = $"{fileId:N}{ext}";
            var absPath = Path.Combine(baseDir, diskFileName);

            await using (var stream = File.Create(absPath))
            {
                await f.CopyToAsync(stream, ct);
            }

            var sha256 = ComputeSha256(absPath);
            var relPath = Path.Combine("pacs", tenantId.ToString("N"), ticketId.ToString("N"), "original", diskFileName)
                .Replace("\\", "/");

            var dto = new TicketFileDto
            {
                Id = fileId,
                TicketId = ticketId,
                TenantId = tenantId,
                OriginalFileName = safeName,
                ContentType = string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType,
                FileSize = f.Length,
                Sha256 = sha256,
                StorageRelPath = relPath,
                OcrStatus = "PENDING"
            };

            await _repo.AddFileAsync(tenantId, dto, ct);

            // Enfileira OCR (seu worker processa pacs.ocr_job e grava pacs.ticket_file.ocr_text/ocr_status)
            await _ocr.EnqueuePacsAsync(tenantId, ticketId, fileId, relPath, ct);
        }

        return ticketId;
    }

    private static string MakeSafeFileName(string name)
    {
        name = (name ?? "file").Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 200 ? name[..200] : name;
    }

    private static string ComputeSha256(string absPath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(absPath);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}