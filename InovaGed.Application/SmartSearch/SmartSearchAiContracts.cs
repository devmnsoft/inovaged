namespace InovaGed.Application.SmartSearch;

public sealed class SmartSearchAiOptions
{
    public const string SectionName = "SmartSearch:Ai";

    public bool Enabled { get; set; }
    /// <summary>OpenAI-compatible provider label shown in the UI (OpenAI, xAI, Ollama, Azure, Groq).</summary>
    public string Provider { get; set; } = "OpenAICompatible";
    /// <summary>Base URL ending before /chat/completions. Examples: https://api.openai.com/v1, https://api.x.ai/v1, http://127.0.0.1:11434/v1</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxTokens { get; set; } = 700;
    public double Temperature { get; set; } = 0.2;
}

public sealed class SmartSearchAiSynthesisRequest
{
    public string Question { get; set; } = string.Empty;
    public string Mode { get; set; } = "search";
    public IReadOnlyList<SmartSearchAiPassage> Passages { get; set; } = [];
}

public sealed class SmartSearchAiPassage
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
}

public sealed class SmartSearchAiSynthesisResult
{
    public bool UsedProvider { get; set; }
    public string? Provider { get; set; }
    public string? Answer { get; set; }
}

public interface ISmartSearchAnswerSynthesizer
{
    bool IsEnabled { get; }
    string ProviderLabel { get; }
    Task<SmartSearchAiSynthesisResult> SynthesizeAsync(SmartSearchAiSynthesisRequest request, CancellationToken ct);
}
