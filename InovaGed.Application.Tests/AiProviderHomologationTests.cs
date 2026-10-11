using System.Net;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Infrastructure.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class AiProviderHomologationTests
{
    [Fact]
    public async Task Rate_limit_429_is_mapped_to_rate_limited_failure_kind()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-key");
        var handler = new MockHttpHandler("{}", HttpStatusCode.TooManyRequests);
        var gateway = CreateGateway(EnabledOptions("Groq"), handler);
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);

        Assert.Equal(AiFailureKind.RateLimited, result.Failure);
        Assert.Equal(1, handler.Calls);
        Assert.True(result.ProviderReached);
    }

    [Fact]
    public async Task Unauthorized_401_is_mapped_to_invalid_credential()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "bad-key");
        var handler = new MockHttpHandler("{}", HttpStatusCode.Unauthorized);
        var gateway = CreateGateway(EnabledOptions("Groq"), handler);
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);

        Assert.Equal(AiFailureKind.InvalidCredential, result.Failure);
        Assert.Equal(1, handler.Calls);
        Assert.True(result.ProviderReached);
    }

    [Fact]
    public async Task Service_unavailable_503_is_mapped_to_provider_unavailable()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-key");
        var handler = new MockHttpHandler("{}", HttpStatusCode.ServiceUnavailable);
        var gateway = CreateGateway(EnabledOptions("Groq"), handler);
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);

        Assert.Equal(AiFailureKind.ProviderUnavailable, result.Failure);
        Assert.Equal(1, handler.Calls);
        Assert.True(result.ProviderReached);
    }

    [Fact]
    public async Task Bad_gateway_502_is_mapped_to_provider_unavailable()
    {
        using var key = AiKeyScope.Set("GEMINI_API_KEY", "test-key");
        var handler = new MockHttpHandler("{}", HttpStatusCode.BadGateway);
        var gateway = CreateGateway(EnabledOptions("Gemini"), handler);
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);

        Assert.Equal(AiFailureKind.ProviderUnavailable, result.Failure);
        Assert.Equal(1, handler.Calls);
        Assert.True(result.ProviderReached);
    }

    [Fact]
    public async Task Missing_credentials_fails_locally_without_network_call()
    {
        using var key = AiKeyScope.Set("DEEPSEEK_API_KEY", "");
        var handler = new MockHttpHandler("{}", HttpStatusCode.OK);
        var gateway = CreateGateway(EnabledOptions("DeepSeek"), handler);
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);

        Assert.Equal(AiFailureKind.CredentialMissing, result.Failure);
        Assert.Equal(0, handler.Calls);
        Assert.False(result.ProviderReached);
    }

    [Fact]
    public async Task Unallowed_model_fails_before_network_with_model_unavailable()
    {
        using var key = AiKeyScope.Set("GROQ_API_KEY", "test-key");
        var handler = new MockHttpHandler("{}", HttpStatusCode.OK);
        var options = EnabledOptions("Groq");
        options.TaskModels["Summarize"] = "forbidden-experimental-model";
        var gateway = CreateGateway(options, handler);
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);

        Assert.Equal(AiFailureKind.ModelUnavailable, result.Failure);
        Assert.Equal(0, handler.Calls);
        Assert.False(result.ProviderReached);
    }

    private static DocumentAiOptions EnabledOptions(string provider)
    {
        var options = new DocumentAiOptions { Enabled = true, Provider = provider };
        options.TaskModels["Summarize"] = "homolog-model";
        options.Providers[provider] = new AiProviderOptions
        {
            Enabled = true,
            BaseUrl = provider == "Gemini" ? "https://generativelanguage.googleapis.com/v1beta"
                    : provider == "Groq" ? "https://api.groq.com/openai/v1"
                    : "https://api.deepseek.com",
            AllowedModels = ["homolog-model"],
            StructuredOutputModels = ["homolog-model"]
        };
        return options;
    }

    private static DocumentAiGateway CreateGateway(DocumentAiOptions options, HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(options), NullLogger<DocumentAiGateway>.Instance);

    private sealed class MockHttpHandler(string payload, HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload)
            });
        }
    }

    private sealed class AiKeyScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;
        private AiKeyScope(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
        public static AiKeyScope Set(string name, string value) => new(name, value);
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
