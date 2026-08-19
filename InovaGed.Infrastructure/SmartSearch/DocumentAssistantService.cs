using InovaGed.Application.SmartSearch;
using Microsoft.Extensions.Caching.Memory;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class DocumentAssistantService : IDocumentAssistantService
{
    private readonly ISmartSearchService _search;
    private readonly IMemoryCache? _cache;
    private static readonly DocumentAssistantSuggestion[] DefaultSuggestions =
    [
        new() { Text = "Quais documentos estão sem OCR?", Category = "OCR" },
        new() { Text = "Mostre arquivos enviados este mês", Category = "Período" },
        new() { Text = "Encontre documentos sem classificação", Category = "Classificação" },
        new() { Text = "Quais documentos estão prontos para auditoria?", Category = "Auditoria" },
        new() { Text = "Quais documentos precisam de ação?", Category = "Qualidade" }
    ];

    public DocumentAssistantService(ISmartSearchService search) : this(search, null) { }
    public DocumentAssistantService(ISmartSearchService search, IMemoryCache? cache) { _search = search; _cache = cache; }

    public async Task<DocumentAssistantResponse> AskAsync(DocumentAssistantQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Question))
            throw new ArgumentException("Escreva uma pergunta sobre os documentos.", nameof(query.Question));

        var security = query.SecurityContext ?? new DocumentAssistantSecurityContext
        {
            TenantId = query.TenantId, UserId = query.UserId, CanReadOcr = true,
            CanViewRestrictedDocuments = query.IsAdmin
        };
        if (security.TenantId != query.TenantId || security.UserId != query.UserId)
            throw new InvalidOperationException("O contexto de segurança não corresponde à sessão atual.");

        var question = query.Question.Trim();
        var conversationId = NormalizeConversationId(query.ConversationId);
        var stateKey = $"assistant-state:{query.TenantId:N}:{query.UserId:N}:{conversationId}";
        var effectiveQuestion = ResolveFollowUp(question, stateKey);
        var cacheKey = $"assistant:{query.TenantId:N}:{query.UserId:N}:{query.Page}:{query.PageSize}:{query.FolderId}:{security.CanReadOcr}:{effectiveQuestion.ToUpperInvariant()}";
        if (_cache?.TryGetValue(cacheKey, out SmartSearchResult? cached) == true && cached is not null)
            return BuildResponse(query, security, cached, question, conversationId, effectiveQuestion);

        var result = await _search.SearchAsync(new SmartSearchRequest
        {
            TenantId = query.TenantId, UserId = query.UserId, IsAdmin = query.IsAdmin,
            Query = effectiveQuestion, FolderId = query.FolderId, Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 20), IncludeOcr = security.CanReadOcr,
            IncludeMetadata = true, Source = query.FolderId.HasValue ? "folder" : "DOCUMENT_ASSISTANT"
        }, ct);
        _cache?.Set(cacheKey, result, TimeSpan.FromSeconds(30));
        _cache?.Set(stateKey, effectiveQuestion, TimeSpan.FromHours(2));
        return BuildResponse(query, security, result, question, conversationId, effectiveQuestion);
    }

    private string ResolveFollowUp(string question, string stateKey)
    {
        if (_cache?.TryGetValue(stateKey, out string? previous) != true || string.IsNullOrWhiteSpace(previous)) return question;
        var normalized = SmartSearchTextNormalizer.Normalize(question);
        var isFollowUp = normalized.StartsWith("e ") || normalized.StartsWith("agora ") || normalized.StartsWith("apenas ")
            || normalized.StartsWith("somente ") || normalized.Contains("desses") || normalized.Contains("tambem");
        return isFollowUp ? $"{previous}. Refinar: {question}" : question;
    }

    private static DocumentAssistantResponse BuildResponse(DocumentAssistantQuery query, DocumentAssistantSecurityContext security, SmartSearchResult result, string question, string conversationId, string effectiveQuestion)
    {
        var sources = result.Items.Select(item => new DocumentAssistantSource
        {
            DocumentId = item.DocumentId, VersionId = item.VersionId, Title = item.Title,
            FileName = item.FileName, FolderName = item.FolderName, DocumentType = item.DocumentType,
            HasOcr = security.CanReadOcr && item.HasOcr,
            Relevance = item.Score,
            Badges = BuildBadges(item),
            OcrExcerpt = security.CanReadOcr ? Limit(item.OcrSnippet, 280) : null,
            MatchReason = item.Reasons.Count == 0 ? "Correspondência encontrada nos metadados autorizados."
                : string.Join("; ", item.Reasons.Take(3).Select(x => $"{x.Reason}: {x.Evidence}"))
        }).ToArray();
        var answer = sources.Length == 0
            ? "Não encontrei evidências nos documentos aos quais você tem acesso. Tente informar tipo, setor, pessoa ou período."
            : $"Encontrei {result.Total} documento(s) compatível(is). Confira as fontes antes de usar a informação.";
        var messages = query.History.TakeLast(8).Concat([
            new DocumentAssistantMessage { Role = "user", Content = question },
            new DocumentAssistantMessage { Role = "assistant", Content = answer }
        ]).ToArray();
        var criteria = new DocumentAssistantCriteria
        {
            OriginalQuestion = question, DocumentType = result.Intent.DocumentType, From = result.Intent.From,
            To = result.Intent.To, UsedOcr = security.CanReadOcr && result.SearchedOcr,
            UsedMetadata = true, IsSensitive = result.Intent.ClinicalTerms.Count > 0 || !string.IsNullOrWhiteSpace(result.Intent.PatientName)
        };
        var actions = new List<DocumentAssistantAction>
        {
            new() { Label = "Usar como filtro", Kind = "filter", Url = $"/SmartSearch?q={Uri.EscapeDataString(question)}", Value = question },
            new() { Label = "Exportar resultado", Kind = "export", Value = answer },
            new() { Label = "Salvar busca", Kind = "save-search", Value = question },
            new() { Label = "Ver documentos com OCR pendente", Kind = "search", Url = "/SmartSearch?q=Quais%20documentos%20est%C3%A3o%20sem%20OCR%3F" },
            new() { Label = "Ver possíveis glosas", Kind = "search", Url = "/SmartSearch?q=Mostre%20faturas%20hospitalares%20com%20poss%C3%ADvel%20glosa" }
        };
        var first = sources.FirstOrDefault();
        if (first is not null)
        {
            if (security.CanClassifyDocuments)
                actions.Add(ConfirmAction("Classificar este documento", "classify", $"/Ged/Details/{first.DocumentId}#classification", "Abra o documento e confirme a classificação sugerida antes de aplicá-la."));
            if (security.CanManageProtocols)
                actions.Add(ConfirmAction("Abrir protocolo", "protocol", $"/Protocols/Create?documentId={first.DocumentId}", "Deseja iniciar a abertura de protocolo para o primeiro resultado? Nenhum protocolo será criado sem sua confirmação na próxima tela."));
            if (security.CanReviewHospitalBilling && first.Badges.Any(x => x is "Hospitalar" or "Faturamento" or "Glosa"))
                actions.Add(ConfirmAction("Enviar para revisão de faturamento", "billing", $"/HospitalBilling/Review?documentId={first.DocumentId}", "Deseja abrir a revisão de faturamento? O envio somente ocorrerá após confirmação na tela de revisão."));
            if (security.CanViewPhysicalArchive)
                actions.Add(new DocumentAssistantAction { Label = "Localizar caixa física", Kind = "physical", Url = $"/Physical/Boxes?documentId={first.DocumentId}" });
        }
        return new DocumentAssistantResponse
        {
            Answer = answer, Criteria = effectiveQuestion == question
                ? (string.IsNullOrWhiteSpace(result.Intent.Explanation)
                    ? "Título, arquivo, pasta, metadados e trechos autorizados de OCR."
                    : result.Intent.Explanation)
                : $"Refinamento da pergunta anterior. {result.Intent.Explanation}",
            Sources = sources, Suggestions = DefaultSuggestions, Total = result.Total, Page = result.Page,
            TotalPages = result.TotalPages, ConversationId = conversationId, AppliedCriteria = criteria,
            Messages = messages, Actions = actions
        };
    }

    private static DocumentAssistantAction ConfirmAction(string label, string kind, string url, string message) => new()
    {
        Label = label, Kind = kind, Url = url, RequiresConfirmation = true, ConfirmationMessage = message
    };

    private static IReadOnlyList<string> BuildBadges(SmartSearchResultItem item)
    {
        var text = $"{item.Title} {item.DocumentType} {item.Classification} {string.Join(' ', item.Reasons.Select(x => x.Evidence))}".ToLowerInvariant();
        var badges = new List<string> { item.HasOcr ? "OCR" : "Sem OCR" };
        if (text.Contains("hospital")) badges.Add("Hospitalar");
        if (text.Contains("fatur") || text.Contains("nota fiscal")) badges.Add("Faturamento");
        if (text.Contains("glosa")) badges.Add("Glosa");
        if (text.Contains("protocolo")) badges.Add("Protocolo");
        if (text.Contains("caixa") || text.Contains("acervo")) badges.Add("Acervo físico");
        if (text.Contains("temporalidade") || text.Contains("eliminação")) badges.Add("Temporalidade");
        if (item.Score < 0.35m) badges.Add("Baixa confiança");
        return badges.Distinct().ToArray();
    }

    private static string NormalizeConversationId(string? value) =>
        Guid.TryParse(value, out var conversationId)
            ? conversationId.ToString("N")
            : Guid.NewGuid().ToString("N");
    private static string? Limit(string? text, int length) => string.IsNullOrWhiteSpace(text) ? null
        : text.Length <= length ? text : $"{text[..length].TrimEnd()}…";
}
