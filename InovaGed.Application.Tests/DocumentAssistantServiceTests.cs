using InovaGed.Application.SmartSearch;
using InovaGed.Infrastructure.SmartSearch;

namespace InovaGed.Application.Tests;

public sealed class DocumentAssistantServiceTests
{
    [Fact]
    public async Task AskAsync_ReturnsGroundedSourcesAndLimitsOcrExcerpt()
    {
        var documentId = Guid.NewGuid();
        var search = new RecordingSearchService(new SmartSearchResult
        {
            Total = 1, Page = 1, TotalPages = 1,
            Intent = new SmartSearchIntent { Explanation = "OCR e metadados" },
            Items = [new SmartSearchResultItem { DocumentId = documentId, Title = "Laudo", HasOcr = true, OcrSnippet = new string('a', 400), Reasons = [new SmartSearchResultReason { Reason = "OCR", Evidence = "termo localizado" }] }]
        });

        var response = await new DocumentAssistantService(search).AskAsync(new DocumentAssistantQuery
        {
            TenantId = Guid.NewGuid(), UserId = Guid.NewGuid(), Question = "localize o laudo"
        }, CancellationToken.None);

        Assert.True(response.HasEvidence);
        Assert.Equal(documentId, response.Sources.Single().DocumentId);
        Assert.EndsWith("…", response.Sources.Single().OcrExcerpt);
        Assert.Contains("OCR", response.Criteria);
    }

    [Fact]
    public async Task AskAsync_ForwardsTenantUserAndPermissionScope()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var search = new RecordingSearchService(new SmartSearchResult());

        await new DocumentAssistantService(search).AskAsync(new DocumentAssistantQuery
        {
            TenantId = tenantId, UserId = userId, IsAdmin = false, Question = "documentos sem OCR"
        }, CancellationToken.None);

        Assert.Equal(tenantId, search.Request!.TenantId);
        Assert.Equal(userId, search.Request.UserId);
        Assert.False(search.Request.IsAdmin);
    }

    [Fact]
    public async Task AskAsync_HidesOcrWhenSecurityContextDoesNotAllowReadingIt()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var search = new RecordingSearchService(new SmartSearchResult
        {
            Total = 1,
            Items = [new SmartSearchResultItem { DocumentId = Guid.NewGuid(), Title = "Restrito", HasOcr = true, OcrSnippet = "conteúdo clínico" }]
        });
        var response = await new DocumentAssistantService(search).AskAsync(new DocumentAssistantQuery
        {
            TenantId = tenantId, UserId = userId, Question = "localize",
            SecurityContext = new DocumentAssistantSecurityContext { TenantId = tenantId, UserId = userId, CanReadOcr = false }
        }, CancellationToken.None);
        Assert.False(search.Request!.IncludeOcr);
        Assert.False(response.Sources.Single().HasOcr);
        Assert.Null(response.Sources.Single().OcrExcerpt);
    }

    [Fact]
    public async Task AskAsync_ReturnsConversationCriteriaAndWorkingActions()
    {
        var search = new RecordingSearchService(new SmartSearchResult
        {
            Intent = new SmartSearchIntent { DocumentType = "PDF", ClinicalTerms = ["termo"] }
        });
        var response = await new DocumentAssistantService(search).AskAsync(new DocumentAssistantQuery
        {
            TenantId = Guid.NewGuid(), UserId = Guid.NewGuid(), Question = "PDF com termo"
        }, CancellationToken.None);
        Assert.NotEmpty(response.ConversationId);
        Assert.Equal("PDF", response.AppliedCriteria.DocumentType);
        Assert.True(response.AppliedCriteria.IsSensitive);
        Assert.Contains(response.Actions, action => action.Kind == "filter" && action.Url!.StartsWith("/SmartSearch?q="));
        Assert.Equal(["user", "assistant"], response.Messages.Select(message => message.Role));
    }

    private sealed class RecordingSearchService(SmartSearchResult result) : ISmartSearchService
    {
        public SmartSearchRequest? Request { get; private set; }
        public Task<SmartSearchResult> SearchAsync(SmartSearchRequest request, CancellationToken ct) { Request = request; return Task.FromResult(result); }
        public Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, string? term, CancellationToken ct) => Task.FromResult<IReadOnlyList<SmartSearchSuggestion>>([]);
    }
}
