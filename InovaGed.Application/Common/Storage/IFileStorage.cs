namespace InovaGed.Application.Common.Storage;

public interface IFileStorage
{
    Task<(string storagePath, long sizeBytes, string md5, string sha256)> SaveAsync(
         Stream content,
         string originalFileName,
         string contentType,
         Guid tenantId,
         Guid documentId,
         Guid versionId,
         CancellationToken ct);

    // ✅ NOVO: salvar arquivo DERIVADO em um path fixo (ex: previews)
    Task<(long sizeBytes, string md5, string sha256)> SaveDerivedAsync(
        string storagePath,
        Stream content,
        string contentType,
        CancellationToken ct);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct);
    Task<bool> ExistsAsync(string storagePath, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
}
