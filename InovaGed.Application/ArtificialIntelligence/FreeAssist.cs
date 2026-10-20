namespace InovaGed.Application.ArtificialIntelligence;

public sealed class FreeAssistOptions
{
    public const string SectionName = "FreeAssist";
    public bool Enabled { get; set; }
    public bool AssistSearch { get; set; } = true;
    public bool AssistOcr { get; set; } = true;
    public string Provider { get; set; } = "Groq";
    public string Model { get; set; } = "openai/gpt-oss-20b";
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public int TimeoutSeconds { get; set; } = 12;
}

public interface IFreeAssist
{
    bool CanAssistSearch { get; }
    bool CanAssistOcr { get; }
    Task<IReadOnlyList<string>> ExpandSearchTermsAsync(string query, CancellationToken ct);
    Task<string?> CleanOcrTextAsync(string text, CancellationToken ct);
}

public static class FreeAssistPolicy
{
    public const int MaxSearchTerms = 6;
    public const int MaxOcrCharacters = 2000;

    public static IReadOnlyList<string> AcceptSearchTerms(string query, IEnumerable<string> candidates)
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { query.Trim() };
        var accepted = new List<string>();
        foreach (var candidate in candidates)
        {
            var term = (candidate ?? string.Empty).Trim();
            if (term.Length is < 2 or > 40) continue;
            if (term.Any(char.IsDigit) && term.Count(char.IsDigit) >= 4) continue;
            if (term.Any(ch => !char.IsLetterOrDigit(ch) && ch is not ' ' and not '-')) continue;
            if (!blocked.Add(term)) continue;
            accepted.Add(term);
            if (accepted.Count == MaxSearchTerms) break;
        }
        return accepted;
    }

    public static string? AcceptOcrRewrite(string original, string? candidate)
    {
        var source = (original ?? string.Empty).Trim();
        var rewritten = (candidate ?? string.Empty).Trim();
        if (source.Length is < 8 or > MaxOcrCharacters) return null;
        if (rewritten.Length < 8) return null;
        var ratio = rewritten.Length / (double)source.Length;
        if (ratio is < 0.85 or > 1.15) return null;
        return string.Equals(source, rewritten, StringComparison.Ordinal) ? null : rewritten;
    }
}
