namespace InovaGed.Application.Classification;

public sealed class HybridDocumentTypeSuggester
{
    private readonly SimpleTextDocumentTypeSuggester _text;

    public HybridDocumentTypeSuggester(SimpleTextDocumentTypeSuggester text)
    {
        _text = text;
    }

    /// <summary>
    /// Sugere tipo(s) documental(is) de forma assíncrona para manter o contrato async/await
    /// dos serviços de aplicação/infra. Atualmente a heurística é síncrona (texto), mas este
    /// método já prepara a evolução para regras híbridas adicionais sem quebrar chamadas.
    /// </summary>
    public Task<IReadOnlyList<DocumentTypeSuggestionDto>> SuggestAsync(
        string? ocrText,
        string? fileName,
        string? folderName,
        string? title,
        IReadOnlyList<(Guid id, string name)> types,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var suggestion = SuggestHybrid(ocrText, fileName, folderName, title, types);

        // Resolve nome do tipo para retorno da API.
        var typeName = suggestion.SuggestedTypeId is Guid suggestedTypeId
            ? types.FirstOrDefault(t => t.id == suggestedTypeId).name
            : null;

        IReadOnlyList<DocumentTypeSuggestionDto> result =
        [
            new DocumentTypeSuggestionDto(
                TypeId: suggestion.SuggestedTypeId,
                TypeName: string.IsNullOrWhiteSpace(typeName) ? null : typeName,
                Confidence: suggestion.Confidence,
                Summary: suggestion.Summary)
        ];

        return Task.FromResult(result);
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
