namespace InovaGed.Infrastructure.ArtificialIntelligence;

/// <summary>Shared egress boundary for provider calls and administrative probes.</summary>
public static class AiEndpointPolicy
{
    public static bool IsLocal(string provider) => provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowed(string provider, string value, IReadOnlyCollection<string> trustedHosts)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        if (IsLocal(provider))
            return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && uri.IsLoopback && uri.AbsolutePath == "/";
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        var official = provider.ToUpperInvariant() switch
        {
            "GROQ" => "api.groq.com", "GEMINI" => "generativelanguage.googleapis.com",
            "DEEPSEEK" => "api.deepseek.com", _ => ""
        };
        return !string.IsNullOrEmpty(official) && (uri.Host.Equals(official, StringComparison.OrdinalIgnoreCase)
            || trustedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase));
    }
}
