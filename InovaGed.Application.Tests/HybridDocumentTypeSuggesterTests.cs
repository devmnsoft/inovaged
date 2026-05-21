using InovaGed.Application.Classification;

namespace InovaGed.Application.Tests;

public class HybridDocumentTypeSuggesterTests
{
    [Fact]
    public async Task SuggestAsync_ReturnsRankedSuggestions_WhenContextMatchesTypeName()
    {
        var sut = new HybridDocumentTypeSuggester(new SimpleTextDocumentTypeSuggester());
        var nfId = Guid.NewGuid();
        var contratoId = Guid.NewGuid();

        var result = await sut.SuggestAsync(
            "Documento referente a nota fiscal com chave de acesso.",
            "nota_fiscal_maio.pdf",
            "financeiro",
            "Nota fiscal de fornecedor",
            new List<(Guid, string)> { (nfId, "Nota Fiscal"), (contratoId, "Contrato") },
            CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.Equal(nfId, result.First().TypeId);
        Assert.True((result.First().Confidence ?? 0m) > 0m);
    }
}
