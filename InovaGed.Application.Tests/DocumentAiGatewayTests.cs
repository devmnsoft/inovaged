using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Infrastructure.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InovaGed.Application.Tests;

public sealed class DocumentAiGatewayTests
{
    [Fact]
    public void Capability_catalog_does_not_claim_images_or_embeddings_for_text_only_providers()
    {
        var gateway = Create(new DocumentAiOptions());
        Assert.False(gateway.Capabilities["Groq"].Image);
        Assert.False(gateway.Capabilities["Groq"].Embeddings);
        Assert.True(gateway.Capabilities["Gemini"].Image);
        Assert.False(gateway.Capabilities["DeepSeek"].Image);
        Assert.False(gateway.Capabilities["DeepSeek"].Embeddings);
    }

    [Fact]
    public async Task Disabled_configuration_never_calls_external_provider()
    {
        var handler = new CountingHandler();
        var gateway = Create(new DocumentAiOptions { Enabled = false }, handler);
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);
        Assert.Equal(AiFailureKind.Disabled, result.Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Model_must_be_explicitly_allowed_before_any_external_call()
    {
        var handler = new CountingHandler();
        var options = new DocumentAiOptions { Enabled = true, Provider = "Groq", TaskModels = { ["Summarize"] = "not-allowed" } };
        options.Providers["Groq"] = new AiProviderOptions { Enabled = true, BaseUrl = "https://api.groq.com/openai/v1" };
        var result = await Create(options, handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);
        Assert.Equal(AiFailureKind.ModelUnavailable, result.Failure);
        Assert.Equal(0, handler.Calls);
    }

    private static DocumentAiGateway Create(DocumentAiOptions options, HttpMessageHandler? handler = null) =>
        new(new HttpClient(handler ?? new CountingHandler()), Options.Create(options), NullLogger<DocumentAiGateway>.Instance);

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)); }
    }
}
