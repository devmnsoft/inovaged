namespace InovaGed.Application
{

    public interface IPreviewGenerator
    {
        /// <summary>
        /// Gera (se necessário) um PDF de preview para qualquer arquivo não-imagem.
        /// Retorna o storagePath do PDF gerado (cacheado).
        /// </summary>
        Task<string> GetOrCreatePreviewPdfAsync(
            Guid tenantId,
            Guid documentId,
            Guid versionId,
            string sourceStoragePath,
            string originalFileName,
            CancellationToken ct);
    }
}
