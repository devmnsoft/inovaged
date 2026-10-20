using System.Net;
using System.Text.Json;
using InovaGed.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace InovaGed.Application.Tests;

public sealed class AiProviderConnectivityTests
{
    [Theory]
    [InlineData("local-test:1", true)]
    [InlineData("missing-test:1", false)]
    public async Task Connectivity_checks_model_against_the_current_local_catalog(string model, bool expected)
    {
        var handler = new Handler();
        var controller = Controller(handler);
        var response = Assert.IsType<OkObjectResult>(await controller.TestConnectivity("Ollama", model));
        var json = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(expected, json.GetProperty("success").GetBoolean());
        Assert.Contains("local-test:1", json.GetProperty("availableModels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("http://127.0.0.1:11434/api/tags", handler.Uri);
    }

    [Fact]
    public async Task Connectivity_cannot_send_to_an_unapproved_endpoint()
    {
        var handler = new Handler();
        var response = Assert.IsType<OkObjectResult>(await Controller(handler, "https://external.example").TestConnectivity("Ollama", "local-test:1"));
        var json = JsonSerializer.SerializeToElement(response.Value);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("Disabled", json.GetProperty("errorKind").GetString());
        Assert.Null(handler.Uri);
    }

    private static AiAdministrationController Controller(Handler handler, string endpoint = "http://127.0.0.1:11434")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentAi:Providers:Ollama:Enabled"] = "true",
            ["DocumentAi:Providers:Ollama:BaseUrl"] = endpoint,
            ["DocumentAi:Providers:Ollama:AllowedModels:0"] = "local-test:1",
            ["DocumentAi:Providers:Ollama:AllowedModels:1"] = "missing-test:1"
        }).Build();
        return new(null!, null!, configuration, new Factory(handler), NullLogger<AiAdministrationController>.Instance);
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string? Uri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"models":[{"name":"local-test:1","digest":"synthetic-digest"}]}""") });
        }
    }
}
