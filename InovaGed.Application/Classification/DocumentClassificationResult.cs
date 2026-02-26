namespace InovaGed.Application.Classification;

public sealed class DocumentClassificationResult
{
    // Resultado final (se você quiser usar também)
    public Guid? DocumentTypeId { get; set; }
    public decimal? Confidence { get; set; }

    // Metadados do método
    public string Method { get; set; } = "RULES";

    // ✅ era init -> agora set (corrige CS8852)
    public string Summary { get; set; } = "";

    public string Source { get; set; } = "OCR";

    public List<string> Tags { get; set; } = new();
    public Dictionary<string, (string Value, decimal? Confidence)> Metadata { get; set; } = new();

    // Sugestão (quando houver)
    public Guid? SuggestedTypeId { get; set; } // ✅ deixe nullable (nem sempre tem)
    public bool HasSuggestion { get; set; }
    public decimal? SuggestedConfidence { get; set; }

    // Contexto
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid DocumentVersionId { get; set; }
}