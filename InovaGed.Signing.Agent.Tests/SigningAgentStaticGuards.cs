using Xunit;

namespace InovaGed.Signing.Agent.Tests;

public sealed class SigningAgentStaticGuards
{
    private static readonly string ProgramText = File.ReadAllText(Path.Combine("..", "InovaGed.Signing.Agent", "Program.cs"));

    [Fact]
    public void Agent_rejects_non_loopback_listeners()
    {
        Assert.Contains("IPAddress.IsLoopback", ProgramText);
        Assert.Contains("só pode escutar", ProgramText);
        Assert.DoesNotContain("AllowAnyOrigin", ProgramText);
    }

    [Fact]
    public void Agent_exposes_required_runtime_endpoints()
    {
        foreach (var endpoint in new[] { "/health", "/info", "/pair", "/pairing", "/certificates", "/operations", "/operations/{id:guid}/confirm", "/operations/{id:guid}/cancel" })
        {
            Assert.Contains(endpoint, ProgramText);
        }
    }

    [Fact]
    public void Agent_has_ssrf_guards_for_content_url()
    {
        Assert.Contains("CONTENT_URL_HTTPS_REQUIRED", ProgramText);
        Assert.Contains("CONTENT_URL_CREDENTIALS_FORBIDDEN", ProgramText);
        Assert.Contains("CONTENT_URL_PRIVATE_NETWORK_FORBIDDEN", ProgramText);
    }
}
