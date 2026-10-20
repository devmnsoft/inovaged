using System.Net;
using System.Text.Json;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Infrastructure.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InovaGed.Application.Tests;

public sealed class OllamaDocumentProviderTests
{
    [Fact]
    public void Circuit_breaker_opens_after_repeated_failures_and_recovers_after_cooldown()
    {
        var clock = new Clock();
        var circuit = new AiProviderCircuitBreaker(clock);
        circuit.Record("local", true, 2, TimeSpan.FromSeconds(30));
        Assert.True(circuit.CanExecute("local"));
        circuit.Record("local", true, 2, TimeSpan.FromSeconds(30));
        Assert.False(circuit.CanExecute("local"));
        Assert.True(circuit.CanExecute("other-provider"));
        clock.Now = clock.Now.AddSeconds(31);
        Assert.True(circuit.CanExecute("local"));
        circuit.Record("local", false, 2, TimeSpan.FromSeconds(30));
        Assert.True(circuit.CanExecute("local"));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Native_local_request_uses_schema_and_preserves_provider_usage()
    {
        var handler = new Handler("""{"done":true,"done_reason":"stop","message":{"content":"{\"status\":\"ok\"}"},"prompt_eval_count":20,"eval_count":5}""");
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["status"],"properties":{"status":{"type":"string","enum":["ok"]}}}""");
        var result = await Gateway(handler).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.ExtractMetadata,
            "Extraia sem inferir dados ausentes", [new("doc:1/p:1", "Documento sintético")], schema), default);
        Assert.True(result.Success, result.Limitation);
        Assert.Equal(25, result.Usage!.TotalTokens);
        Assert.Equal("Ollama", result.Provider);
        Assert.Equal("http://127.0.0.1:11434/api/chat", handler.Uri);
        Assert.Null(handler.Authorization);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("object", body.RootElement.GetProperty("format").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("http://10.0.0.1:11434")]
    [InlineData("https://ollama.com")]
    [InlineData("http://127.0.0.1:11434/proxy")]
    [InlineData("http://user:password@localhost:11434")]
    [InlineData("http://127.0.0.1:11434?redirect=external")]
    public async Task Local_provider_rejects_nonlocal_or_ambiguous_endpoints_before_send(string endpoint)
    {
        var handler = new Handler("{}");
        var result = await Gateway(handler, endpoint).ExecuteAsync(Request(), default);
        Assert.Equal(AiFailureKind.Disabled, result.Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Local_provider_rejects_cloud_model_before_send()
    {
        var handler = new Handler("{}");
        var result = await Gateway(handler, model: "model:cloud").ExecuteAsync(Request(), default);
        Assert.Equal(AiFailureKind.ModelUnavailable, result.Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("""{"done":false,"message":{"content":"parcial"}}""")]
    [InlineData("""{"done":true,"done_reason":"length","message":{"content":"parcial"}}""")]
    [InlineData("""{"done":true,"done_reason":"stop","message":{"content":""}}""")]
    public async Task Incomplete_or_empty_local_response_is_rejected(string response)
    {
        var result = await Gateway(new Handler(response)).ExecuteAsync(Request(), default);
        Assert.Equal(AiFailureKind.InvalidOutput, result.Failure);
    }

    private static AiRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), AiTask.ExtractMetadata, "Extraia", []);
    private static DocumentAiGateway Gateway(Handler handler, string endpoint = "http://127.0.0.1:11434", string model = "local-test:1") =>
        new(new HttpClient(handler), Options.Create(new DocumentAiOptions
        {
            Enabled = true, Provider = "Ollama", TaskModels = new() { [nameof(AiTask.ExtractMetadata)] = model },
            Providers = new() { ["Ollama"] = new() { Enabled = true, BaseUrl = endpoint, AllowedModels = [model], StructuredOutputModels = [model] } }
        }), NullLogger<DocumentAiGateway>.Instance);
    private sealed class Handler(string response) : HttpMessageHandler
    {
        public int Calls;
        public string? Uri, Body, Authorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Uri = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(ct);
            Authorization = request.Headers.Authorization?.ToString();
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
