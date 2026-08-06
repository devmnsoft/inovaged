using InovaGed.Application.SmartSearch;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class DocumentAssistantService(ISmartSearchService search) : IDocumentAssistantService
{
    private static readonly DocumentAssistantSuggestion[] DefaultSuggestions =
    [
        new() { Text = "Quais documentos estão sem OCR?", Category = "OCR" },
        new() { Text = "Mostre arquivos enviados este mês", Category = "Período" },
        new() { Text = "Encontre documentos sem classificação", Category = "Classificação" },
        new() { Text = "Quais documentos precisam de revisão?", Category = "Qualidade" }
    ];

    public async Task<DocumentAssistantResponse> AskAsync(DocumentAssistantQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Question))
            throw new ArgumentException("Escreva uma pergunta sobre os documentos.", nameof(query.Question));

        var result = await search.SearchAsync(new SmartSearchRequest
        {
            TenantId = query.TenantId,
            UserId = query.UserId,
            IsAdmin = query.IsAdmin,
            Query = query.Question.Trim(),
            FolderId = query.FolderId,
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 20),
            IncludeOcr = true,
            IncludeMetadata = true,
            Source = query.FolderId.HasValue ? "folder" : "DOCUMENT_ASSISTANT"
        }, ct);

        var sources = result.Items.Select(item => new DocumentAssistantSource
        {
            DocumentId = item.DocumentId,
            VersionId = item.VersionId,
            Title = item.Title,
            FileName = item.FileName,
            FolderName = item.FolderName,
            DocumentType = item.DocumentType,
            HasOcr = item.HasOcr,
            OcrExcerpt = Limit(item.OcrSnippet, 280),
            MatchReason = item.Reasons.Count == 0
                ? "Correspondência encontrada nos metadados autorizados."
                : string.Join("; ", item.Reasons.Take(3).Select(x => $"{x.Reason}: {x.Evidence}"))
        }).ToArray();

        return new DocumentAssistantResponse
        {
            Answer = sources.Length == 0
                ? "Não encontrei evidências nos documentos aos quais você tem acesso. Tente informar tipo, setor, paciente ou período."
                : $"Encontrei {result.Total} documento(s) compatível(is). Confira as fontes antes de usar a informação.",
            Criteria = string.IsNullOrWhiteSpace(result.Intent.Explanation)
                ? "Título, arquivo, pasta, metadados e trechos limitados de OCR."
                : result.Intent.Explanation,
            Sources = sources,
            Suggestions = DefaultSuggestions,
            Total = result.Total,
            Page = result.Page,
            TotalPages = result.TotalPages
        };
    }

    private static string? Limit(string? text, int length) => string.IsNullOrWhiteSpace(text)
        ? null
        : text.Length <= length ? text : $"{text[..length].TrimEnd()}…";
}
