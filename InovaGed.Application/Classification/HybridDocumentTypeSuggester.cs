namespace InovaGed.Application.Classification;

public sealed class HybridDocumentTypeSuggester
{
    private readonly SimpleTextDocumentTypeSuggester _text;

    public HybridDocumentTypeSuggester(SimpleTextDocumentTypeSuggester text)
    {
        _text = text;
    }

    public SimpleTextDocumentTypeSuggester.Suggestion SuggestHybrid(
        string? ocrText,
        string? fileName,
        string? folderName,
        string? title,
        IReadOnlyList<(Guid id, string name)> types)
    {
        // ✅ hoje: só OCR/Text (porque as regras de pasta você já tem no AppService: AutoClassifyByFolderAsync)
        // Depois a gente adiciona regras por nome/título e mistura scores.

        var s = _text.Suggest(ocrText, types);

        // Se quiser enriquecer o summary:
        if (s.SuggestedTypeId != null && !string.IsNullOrWhiteSpace(fileName))
        {
            return s with
            {
                Summary = (s.Summary ?? s.Method ?? "OCR") + $" | arquivo='{fileName}'"
            };
        }

        return s;
    }
}
