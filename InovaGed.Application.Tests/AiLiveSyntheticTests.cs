using System.Text.Json;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Infrastructure.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InovaGed.Application.Tests;

public sealed class LiveAiFactAttribute : FactAttribute
{
    public LiveAiFactAttribute()
    {
        if (System.Environment.GetEnvironmentVariable("INOVAGED_AI_LIVE_SYNTHETIC") != "1")
            Skip = "Teste real opt-in: INOVAGED_AI_LIVE_SYNTHETIC=1; somente conteúdo sintético.";
    }
}

public sealed class AiLiveSyntheticTests
{
    [LiveAiFact]
    public async Task Gemini_returns_schema_valid_output_for_a_synthetic_document()
    {
        var model = System.Environment.GetEnvironmentVariable("INOVAGED_AI_LIVE_MODEL")
            ?? throw new InvalidOperationException("Escolha explicitamente um modelo do catálogo atual.");
        var options = new DocumentAiOptions
        {
            Enabled = true, Provider = "Gemini", MaximumOutputTokens = 1024, TimeoutSeconds = 60,
            TaskModels = new() { [nameof(AiTask.ExtractMetadata)] = model },
            Providers = new() { ["Gemini"] = new() { Enabled = true,
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta", AllowedModels = [model], StructuredOutputModels = [model] } }
        };
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["documento"],"properties":{"documento":{"type":"string","enum":["TESTE-SINTETICO-001"]}}}""");
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var gateway = new DocumentAiGateway(client, Options.Create(options), NullLogger<DocumentAiGateway>.Instance);
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.ExtractMetadata,
            "Retorne somente JSON com o identificador literal do documento. Não complete campos ausentes.",
            [new("sintetico", "Documento de teste sem dados pessoais. Identificador: TESTE-SINTETICO-001")], schema), default);
        Assert.True(result.Success, $"{result.Failure}: {result.Limitation}");
        Assert.Equal("TESTE-SINTETICO-001", result.StructuredData!.RootElement.GetProperty("documento").GetString());
        Assert.True(result.ProviderReached);
    }
}
