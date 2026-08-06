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

    private sealed class RecordingSearchService(SmartSearchResult result) : ISmartSearchService
    {
        public SmartSearchRequest? Request { get; private set; }
        public Task<SmartSearchResult> SearchAsync(SmartSearchRequest request, CancellationToken ct) { Request = request; return Task.FromResult(result); }
        public Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, string? term, CancellationToken ct) => Task.FromResult<IReadOnlyList<SmartSearchSuggestion>>([]);
    }
}
