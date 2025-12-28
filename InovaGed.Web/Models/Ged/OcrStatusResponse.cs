namespace InovaGed.Web.Models.Ged
{
    public sealed class OcrStatusResponse
    {
        public string Status { get; set; } = ""; // PENDING / PROCESSING / COMPLETED / ERROR / NONE
        public string? ErrorMessage { get; set; }

        public Guid DocumentId { get; set; }
        public Guid SourceVersionId { get; set; } // versão base (a que o OCR roda)
        public Guid? OcrVersionId { get; set; }   // versão _OCR.pdf, quando existir
    }
}
