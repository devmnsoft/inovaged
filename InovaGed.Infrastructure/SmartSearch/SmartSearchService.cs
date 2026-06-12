using System.Diagnostics;
using InovaGed.Application.SmartSearch;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class SmartSearchService : ISmartSearchService
{
    private readonly ISmartQueryParser _parser;
    private readonly ISmartSearchRepository _repository;

    public SmartSearchService(ISmartQueryParser parser, ISmartSearchRepository repository)
    {
        _parser = parser;
        _repository = repository;
    }

    public async Task<SmartSearchResult> SearchAsync(SmartSearchRequest request, CancellationToken ct)
    {
        request.Page = Math.Max(1, request.Page);
        request.PageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 50);
        var sw = Stopwatch.StartNew();
        var intent = await _parser.ParseAsync(request.TenantId, request.Query, request, ct);
        var result = await _repository.SearchAsync(intent, new UserDocumentScope { TenantId = request.TenantId, UserId = request.UserId, IsAdmin = request.IsAdmin }, request, ct);
        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;
        await _repository.LogQueryAsync(request, intent, result.Total, result.DurationMs, ct);
        return result;
    }

    public Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, string? term, CancellationToken ct)
        => _repository.SuggestAsync(tenantId, term, ct);
}

public sealed class DocumentChatService : IDocumentChatService
{
    private readonly ISmartSearchRepository _repository;
    private readonly ISmartQueryParser _parser;

    public DocumentChatService(ISmartSearchRepository repository, ISmartQueryParser parser)
    {
        _repository = repository;
        _parser = parser;
    }

    public async Task<DocumentQuestionAnswer> AskAsync(Guid tenantId, Guid userId, DocumentQuestionRequest request, CancellationToken ct)
    {
        var ocr = await _repository.GetDocumentOcrAsync(tenantId, request.DocumentId, ct);
        await _repository.LogAccessAsync(tenantId, userId, request.DocumentId, "SMART_SEARCH", "SEARCH_DOCUMENT_QUESTION", ct);
        if (string.IsNullOrWhiteSpace(ocr))
        {
            return new DocumentQuestionAnswer { FoundInDocument = false, Answer = "Não encontrei texto OCR disponível para responder sobre este documento." };
        }

        var intent = await _parser.ParseAsync(tenantId, request.Question, new SmartSearchRequest { TenantId = tenantId, UserId = userId, Query = request.Question }, ct);
        var terms = intent.Keywords.Concat(intent.ClinicalTerms).Where(t => t.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var snippets = BuildSnippets(ocr, terms).ToList();
        if (snippets.Count == 0)
        {
            return new DocumentQuestionAnswer { FoundInDocument = false, Answer = "Não encontrei essa informação no texto OCR deste documento." };
        }

        return new DocumentQuestionAnswer
        {
            FoundInDocument = true,
            EvidenceSnippets = snippets,
            Answer = "Encontrei menções no OCR/metadados do documento relacionadas à pergunta. Veja os trechos destacados abaixo."
        };
    }

    private static IEnumerable<string> BuildSnippets(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0) terms = [""];
        foreach (var term in terms)
        {
            var index = string.IsNullOrWhiteSpace(term) ? 0 : text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var start = Math.Max(0, index - 90);
            var len = Math.Min(text.Length - start, 240);
            yield return text.Substring(start, len).ReplaceLineEndings(" ").Trim();
        }
    }
}

public sealed class SearchStatisticsService : ISearchStatisticsService
{
    private readonly ISmartSearchRepository _repository;
    public SearchStatisticsService(ISmartSearchRepository repository) => _repository = repository;
    public Task<SmartSearchStatistics> GetAsync(Guid tenantId, CancellationToken ct) => _repository.GetStatisticsAsync(tenantId, ct);
}
