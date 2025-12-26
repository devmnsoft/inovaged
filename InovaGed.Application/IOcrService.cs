namespace InovaGed.Application
{
    // Resultado do OCR: PDF pesquisável + texto extraído (opcional)
    public sealed record OcrPdfResult(
        byte[] OcrPdfBytes,
        string ExtractedText
    );

    // Exceção específica para o controller tratar
    public sealed class PdfHasDigitalSignatureException : Exception
    {
        public PdfHasDigitalSignatureException()
            : base("PDF possui assinatura digital e o OCR seria bloqueado sem autorização para invalidar.")
        {
        }
    }

    public interface IOcrService
    {
        /// <summary>
        /// Executa OCR em um PDF (do storage) e retorna um PDF pesquisável (bytes) + texto extraído.
        /// Se invalidateDigitalSignatures=false e o PDF estiver assinado, lança PdfHasDigitalSignatureException.
        /// </summary>
        Task<OcrPdfResult> OcrizePdfAsync(
            string pdfStoragePath,
            bool invalidateDigitalSignatures,
            CancellationToken ct);
    }
}


 
