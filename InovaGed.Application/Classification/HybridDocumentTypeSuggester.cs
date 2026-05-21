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

        var result = SuggestTopAsync(ocrText, fileName, folderName, title, types);

        return Task.FromResult<IReadOnlyList<DocumentTypeSuggestionDto>>(result);
    }

    public IReadOnlyList<DocumentTypeSuggestionDto> SuggestTopAsync(
        string? ocrText,
        string? fileName,
        string? folderName,
        string? title,
        IReadOnlyList<(Guid id, string name)> types)
    {
        if (types.Count == 0)
            return Array.Empty<DocumentTypeSuggestionDto>();

        var tokens = string.Join(' ', new[] { ocrText, fileName, folderName, title }
            .Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

        var ranked = types
            .Select(t =>
            {
                var name = t.name.ToLowerInvariant();
                var score = 0m;

                foreach (var chunk in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tokens.Contains(chunk, StringComparison.OrdinalIgnoreCase))
                        score += 0.18m;
                }

                if (!string.IsNullOrWhiteSpace(fileName) && fileName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    score += 0.20m;

                if (!string.IsNullOrWhiteSpace(title) && title.Contains(name, StringComparison.OrdinalIgnoreCase))
                    score += 0.20m;

                if (!string.IsNullOrWhiteSpace(folderName) && folderName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    score += 0.12m;

                return new DocumentTypeSuggestionDto(t.id, t.name, Math.Min(score, 0.95m), $"Sugestão híbrida baseada em OCR/nome/título/pasta para '{t.name}'.");
            })
            .Where(x => (x.Confidence ?? 0m) > 0m)
            .OrderByDescending(x => x.Confidence)
            .Take(3)
            .ToList();

        if (ranked.Count > 0)
            return ranked;

        var fallback = SuggestHybrid(ocrText, fileName, folderName, title, types);
        var typeName = fallback.SuggestedTypeId is Guid suggestedTypeId
            ? types.FirstOrDefault(t => t.id == suggestedTypeId).name
            : null;

        return new[]
        {
            new DocumentTypeSuggestionDto(fallback.SuggestedTypeId, typeName, fallback.Confidence, fallback.Summary)
        };
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
