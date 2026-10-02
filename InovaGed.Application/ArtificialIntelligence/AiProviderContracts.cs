using System.Text.Json;

namespace InovaGed.Application.ArtificialIntelligence;

public enum AiTask { AskCollection, Summarize, ExtractMetadata, SuggestClassification, SupportProtocol, CompareDocuments }
public enum AiFailureKind { None, Disabled, CredentialMissing, InvalidCredential, ModelUnavailable, QuotaExceeded, RateLimited, Cancelled, Timeout, InvalidOutput, ProviderUnavailable, Internal }

public sealed record AiCapabilities(bool Text, bool Image, bool StructuredOutput, bool Streaming, bool Embeddings);
public sealed record AiUsage(long? InputTokens, long? OutputTokens, long? TotalTokens);
public sealed record AiContextItem(string Reference, string Text, string? MediaType = null, byte[]? Data = null);
public sealed record AiRequest(
    Guid TenantId,
    Guid UserId,
    AiTask Task,
    string Instructions,
    IReadOnlyList<AiContextItem> Context,
    JsonDocument? OutputSchema = null,
    string? IdempotencyKey = null);
public sealed record AiResult(
    bool Success,
    string? Text,
    JsonDocument? StructuredData,
    AiUsage? Usage,
    string Provider,
    string Model,
    AiFailureKind Failure = AiFailureKind.None,
    string? Limitation = null,
    string? CorrelationId = null);

/// <summary>Server-side gateway. Callers must supply only tenant-authorized context.</summary>
public interface IDocumentAiGateway
{
    Task<AiResult> ExecuteAsync(AiRequest request, CancellationToken cancellationToken);
    IReadOnlyDictionary<string, AiCapabilities> Capabilities { get; }
}

public sealed class DocumentAiOptions
{
    public const string SectionName = "DocumentAi";
    public bool Enabled { get; set; }
    public string Provider { get; set; } = string.Empty;
    public Dictionary<string, AiProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> TaskModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TimeoutSeconds { get; set; } = 30;
    public int MaximumInputCharacters { get; set; } = 50_000;
    public int MaximumOutputTokens { get; set; } = 1_000;
    public int MaximumOutputCharacters { get; set; } = 100_000;
    /// <summary>Additional HTTPS hosts approved by global administration. Official provider hosts are always trusted.</summary>
    public List<string> TrustedEndpointHosts { get; set; } = [];
}

public sealed class AiProviderOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public List<string> AllowedModels { get; set; } = [];
}
