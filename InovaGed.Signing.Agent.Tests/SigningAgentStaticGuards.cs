using Xunit;

namespace InovaGed.Signing.Agent.Tests;

public sealed class SigningAgentStaticGuards
{
    private static readonly string ProgramText = LocateAgentSources();

    private static string LocateAgentSources()
    {
        var candidates = new[]
        {
            Path.Combine("..", "InovaGed.Signing.Agent"),
            Path.Combine("..", "..", "..", "..", "InovaGed.Signing.Agent"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "InovaGed.Signing.Agent"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "InovaGed.Signing.Agent"),
            Path.Combine(Directory.GetCurrentDirectory(), "InovaGed.Signing.Agent")
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full))
            {
                var files = Directory.GetFiles(full, "*.cs", SearchOption.TopDirectoryOnly);
                if (files.Length > 0) return string.Join("\n", files.Select(File.ReadAllText));
            }
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, "InovaGed.Signing.Agent");
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.cs", SearchOption.TopDirectoryOnly);
                if (files.Length > 0) return string.Join("\n", files.Select(File.ReadAllText));
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate InovaGed.Signing.Agent source directory.");
    }

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
