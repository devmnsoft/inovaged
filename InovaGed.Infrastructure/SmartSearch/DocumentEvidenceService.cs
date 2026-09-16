using System.Text.RegularExpressions;
using InovaGed.Application.SmartSearch;

namespace InovaGed.Infrastructure.SmartSearch;

/// <summary>Deterministic, provider-free evidence retrieval over the existing SmartSearch and OCR read models.</summary>
public sealed class DocumentEvidenceService : IDocumentEvidenceService
{
    public const int QuestionLimit = 500;
    public const int DocumentLimit = 20;
    public const int PassageLimit = 12;
    private readonly ISmartSearchService _search;
    private readonly ISmartSearchRepository _repository;

    public DocumentEvidenceService(ISmartSearchService search, ISmartSearchRepository repository) { _search = search; _repository = repository; }

    public async Task<DocumentEvidenceResponse> AskAsync(DocumentEvidenceQuery query, CancellationToken ct)
    {
        var question = (query.Question ?? string.Empty).Trim();
        if (question.Length is < 3 or > QuestionLimit) throw new ArgumentException($"A pergunta deve ter entre 3 e {QuestionLimit} caracteres.");
        var maxDocuments = Math.Clamp(query.MaxDocuments, 1, DocumentLimit);
        var maxPassages = Math.Clamp(query.MaxPassages, 1, PassageLimit);
        var candidates = await ResolveScopeAsync(query, question, maxDocuments, ct);
        ct.ThrowIfCancellationRequested();
        var authorized = await _repository.CompareDocumentsAsync(query.TenantId, query.UserId, candidates.Ids.Take(maxDocuments).ToArray(), true, query.IsAdmin, ct);
        if (authorized.Count != candidates.Ids.Take(maxDocuments).Distinct().Count())
            throw new UnauthorizedAccessException("O acesso a uma ou mais fontes mudou. Refaça a consulta.");

        var terms = Terms(question);
        var sources = authorized.Select(document => BuildSource(document, terms)).Where(x => x.Passages.Count > 0)
            .OrderByDescending(x => x.Passages.Max(p => p.Score)).Take(maxDocuments).ToArray();
        var remaining = maxPassages;
        sources = sources.Select(source => { var take = Math.Min(2, remaining); remaining -= take; source.Passages = source.Passages.Take(Math.Max(0, take)).ToArray(); return source; })
            .Where(x => x.Passages.Count > 0).ToArray();
        var partial = candidates.Available > maxDocuments || sources.Sum(x => x.Passages.Count) >= maxPassages;
        var limitations = new List<string>();
        if (candidates.Available > maxDocuments) limitations.Add($"Cobertura parcial: {maxDocuments} de {candidates.Available} documentos foram considerados.");
        if (authorized.Any(x => !x.HasExtractedText)) limitations.Add("Um ou mais documentos não possuem extração de texto disponível.");
        limitations.Add("A extração não preserva vínculo comprovável com páginas; por isso nenhuma página foi inferida.");
        return new DocumentEvidenceResponse
        {
            Status = sources.Length == 0 ? DocumentQuestionStatus.InsufficientEvidence : partial ? DocumentQuestionStatus.PartialCoverage : DocumentQuestionStatus.Completed,
            Message = sources.Length == 0 ? "Não encontrei evidência suficiente nos documentos analisados." : "Confira os trechos e abra cada fonte antes de usar a informação.",
            ScopeLabel = candidates.Label, AvailableDocuments = candidates.Available, ConsideredDocuments = authorized.Count,
            CoveragePartial = partial, Sources = sources, Limitations = limitations
        };
    }

    private async Task<(Guid[] Ids, int Available, string Label)> ResolveScopeAsync(DocumentEvidenceQuery query, string question, int limit, CancellationToken ct)
    {
        if (query.Scope == DocumentQuestionScopeKind.SelectedDocuments)
        {
            var ids = query.DocumentIds.Where(x => x != Guid.Empty).Distinct().ToArray();
            if (ids.Length == 0) throw new ArgumentException("Selecione ao menos um documento.");
            return (ids, ids.Length, $"{ids.Length} documento(s) selecionado(s)");
        }
        if (query.Scope == DocumentQuestionScopeKind.Collection)
        {
            if (!query.CollectionId.HasValue) throw new ArgumentException("Selecione uma coleção de trabalho.");
            var collection = await _repository.GetCollectionAsync(query.TenantId, query.UserId, query.CollectionId.Value, ct)
                ?? throw new ArgumentException("Coleção não encontrada ou fora do seu acesso.");
            return (collection.Items.Select(x => x.DocumentId).Distinct().ToArray(), collection.ItemCount, $"Coleção: {collection.Name}");
        }
        var searchQuery = string.IsNullOrWhiteSpace(query.SearchQuery) ? question : query.SearchQuery.Trim();
        var result = await _search.SearchAsync(new SmartSearchRequest { TenantId = query.TenantId, UserId = query.UserId, IsAdmin = query.IsAdmin, Query = searchQuery, Page = 1, PageSize = limit, IncludeOcr = true, IncludeMetadata = true, Source = "DOCUMENT_EVIDENCE" }, ct);
        return (result.Items.Select(x => x.DocumentId).Distinct().ToArray(), result.Total, $"Todos os resultados da pesquisa “{searchQuery}”");
    }

    public static DocumentEvidenceSource BuildSource(SmartSearchComparisonDocument document, IReadOnlyList<string> terms)
    {
        var passages = BuildPassages(document.ExtractedText, terms).ToArray();
        return new DocumentEvidenceSource { DocumentId = document.DocumentId, VersionId = document.VersionId, VersionNumber = document.VersionNumber,
            Title = document.Title, ExtractedByOcr = document.HasExtractedText, ExtractionIncomplete = !document.HasExtractedText, Passages = passages };
    }

    public static IEnumerable<DocumentEvidencePassage> BuildPassages(string? text, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text) || terms.Count == 0) yield break;
        var found = new List<(int Index, decimal Score)>();
        foreach (var term in terms)
            for (var index = 0; (index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0; index += term.Length)
                found.Add((index, term.Length + terms.Count(t => text.IndexOf(t, Math.Max(0, index - 220), Math.Min(text.Length - Math.Max(0, index - 220), 440), StringComparison.OrdinalIgnoreCase) >= 0) * 10));
        foreach (var match in found.OrderByDescending(x => x.Score).ThenBy(x => x.Index).GroupBy(x => x.Index / 240).Select(x => x.First()).Take(4))
        {
            var start = FindBoundary(text, Math.Max(0, match.Index - 220), false);
            var end = FindBoundary(text, Math.Min(text.Length, match.Index + 320), true);
            var excerpt = Regex.Replace(text[start..end], @"\s+", " ").Trim();
            if (excerpt.Length < 3) continue;
            yield return new DocumentEvidencePassage { Id = $"p-{start}-{end}", Text = excerpt, StartOffset = start, EndOffset = end,
                Page = null, LocationLabel = $"Texto extraído, caracteres {start + 1}–{end}", Score = match.Score };
        }
    }

    private static int FindBoundary(string text, int index, bool forward)
    {
        const string boundaries = ".!?\n";
        if (forward) { for (var i = index; i < Math.Min(text.Length, index + 100); i++) if (boundaries.Contains(text[i])) return i + 1; return index; }
        for (var i = index; i > Math.Max(0, index - 100); i--) if (boundaries.Contains(text[i])) return i + 1; return index;
    }

    private static IReadOnlyList<string> Terms(string value) => Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}/.-]{3,}")
        .Select(x => x.Value).Where(x => !StopWords.Contains(x)).Distinct().Take(12).ToArray();
    private static readonly HashSet<string> StopWords = new(["que","qual","quais","nos","nas","dos","das","uma","para","com","estes","documentos","documento","aparece","consta"], StringComparer.OrdinalIgnoreCase);
}
