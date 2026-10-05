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
        Assert.False(gateway.Capabilities["Gemini"].Image);
        Assert.False(gateway.Capabilities["Gemini"].Streaming);
        Assert.False(gateway.Capabilities["Gemini"].Embeddings);
        Assert.False(gateway.Capabilities["DeepSeek"].Image);
        Assert.False(gateway.Capabilities["DeepSeek"].Embeddings);
    }

    [Fact]
    public async Task Text_adapter_rejects_binary_without_external_call()
    {
        var handler = new CountingHandler(); var options = Enabled("Gemini");
        var result = await Create(options, handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", [new("doc", "ocr", "image/png", [1, 2])]), default);
        Assert.Equal(AiFailureKind.InvalidOutput, result.Failure);
        Assert.Equal(0, handler.Calls);
        Assert.NotNull(result.CorrelationId);
    }

    [Fact]
    public async Task Complete_input_including_instructions_is_limited_before_send()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        var handler = new CountingHandler(); var options = Enabled("Groq"); options.MaximumInputCharacters = 1_000;
        var result = await Create(options, handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, new string('x', 1_001), []), default);
        Assert.Equal(AiFailureKind.QuotaExceeded, result.Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Truncated_completion_is_rejected()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        var handler = new CountingHandler("""{"choices":[{"finish_reason":"length","message":{"content":"parcial"}}]}""");
        var result = await Create(Enabled("Groq"), handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);
        Assert.Equal(AiFailureKind.InvalidOutput, result.Failure);
        Assert.Contains("truncada", result.Limitation);
    }

    [Fact]
    public async Task Syntactically_valid_json_outside_schema_is_rejected()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        var handler = new CountingHandler("""{"choices":[{"finish_reason":"stop","message":{"content":"{\"status\":123,\"extra\":true}"}}]}""");
        using var schema = System.Text.Json.JsonDocument.Parse("""{"type":"object","required":["status"],"additionalProperties":false,"properties":{"status":{"type":"string","enum":["ok"]}}}""");
        var result = await Create(Enabled("Groq"), handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", [], schema), default);
        Assert.Equal(AiFailureKind.InvalidOutput, result.Failure);
    }

    [Fact]
    public async Task Unsupported_schema_keyword_is_rejected_before_network()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        var handler = new CountingHandler();
        using var schema = System.Text.Json.JsonDocument.Parse("""{"type":"string","pattern":"secret"}""");
        var result = await Create(Enabled("Groq"), handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", [], schema), default);
        Assert.Equal(AiFailureKind.InvalidOutput, result.Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task String_and_array_limits_are_enforced_locally()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        var handler = new CountingHandler("""{"choices":[{"finish_reason":"stop","message":{"content":"{\"items\":[\"too long\",\"second\"]}"}}]}""");
        using var schema = System.Text.Json.JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","maxItems":1,"items":{"type":"string","maxLength":3}}}}""");
        var result = await Create(Enabled("Groq"), handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", [], schema), default);
        Assert.Equal(AiFailureKind.InvalidOutput, result.Failure);
    }

    [Fact]
    public async Task User_cancellation_is_propagated_instead_of_becoming_timeout()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(Enabled("Groq"), new WaitingHandler()).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), cts.Token));
    }

    [Fact]
    public async Task Arbitrary_endpoint_is_blocked_before_credentials_or_network()
    {
        var options = Enabled("Groq"); options.Providers["Groq"].BaseUrl = "https://attacker.example/api";
        var handler = new CountingHandler();
        var result = await Create(options, handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);
        Assert.Equal(AiFailureKind.Disabled, result.Failure);
        Assert.Equal(0, handler.Calls);
    }

    private static DocumentAiOptions Enabled(string provider)
    {
        var options = new DocumentAiOptions { Enabled = true, Provider = provider };
        options.TaskModels["Summarize"] = "test-model";
        options.Providers[provider] = new AiProviderOptions { Enabled = true, BaseUrl = provider == "Gemini" ? "https://generativelanguage.googleapis.com/v1beta" : provider == "Groq" ? "https://api.groq.com/openai/v1" : "https://api.deepseek.com", AllowedModels = ["test-model"], StructuredOutputModels = ["test-model"] };
        return options;
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
    public async Task Deterministic_provider_answers_from_the_fixture_and_never_calls_http()
    {
        using var gate = AiKeyScope.Set("INOVAGED_AI_DETERMINISTIC", "1");
        var handler = new CountingHandler();
        var options = new DocumentAiOptions { Enabled = true, Provider = "Deterministic" };
        var sent = false;
        var text = "TITULO: Laudo ficticio\nDESCRICAO: Texto de homologacao\nSIGILOSO\nTIPO: Contrato\nCLASSE: 01.02";
        var request = new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.ExtractMetadata, "extrair", [new("fonte", text)], OnRequestSent: _ => { sent = true; return Task.CompletedTask; });
        var result = await Create(options, handler).ExecuteAsync(request, default);
        Assert.True(result.Success);
        Assert.True(result.ProviderReached);
        Assert.True(sent);
        Assert.Equal(0, handler.Calls);
        Assert.Contains("Laudo ficticio", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("http", result.Provider, StringComparison.OrdinalIgnoreCase);
        var archival = await Create(options, handler).ExecuteAsync(request with { Task = AiTask.SuggestArchivalClassification }, default);
        Assert.Contains("01.02", archival.Text, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Deterministic_provider_stays_disabled_without_the_local_gate()
    {
        using var gate = AiKeyScope.Set("INOVAGED_AI_DETERMINISTIC", "");
        var handler = new CountingHandler();
        var result = await Create(new DocumentAiOptions { Enabled = true, Provider = "Deterministic" }, handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", [new("fonte", "texto")]), default);
        Assert.Equal(AiFailureKind.Disabled, result.Failure);
        Assert.False(result.ProviderReached);
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

    private sealed class CountingHandler(string payload = "{}") : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) }); }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    { protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException(); } }

    private sealed class AiKeyScope : IDisposable
    {
        private readonly string _name; private readonly string? _previous;
        private AiKeyScope(string name, string value) { _name = name; _previous = System.Environment.GetEnvironmentVariable(name); System.Environment.SetEnvironmentVariable(name, value); }
        public static AiKeyScope Set(string name, string value) => new(name, value);
        public void Dispose() => System.Environment.SetEnvironmentVariable(_name, _previous);
    }
}
