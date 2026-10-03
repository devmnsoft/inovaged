using System.Text.Json;

namespace InovaGed.Application.ArtificialIntelligence;

public enum AiTask { AskCollection, Summarize, ExtractMetadata, SuggestClassification, SupportProtocol, CompareDocuments }
public enum AiFailureKind { None, Disabled, CredentialMissing, InvalidCredential, ModelUnavailable, QuotaExceeded, IdempotencyConflict, RateLimited, Cancelled, Timeout, InvalidOutput, ProviderUnavailable, Internal }
public sealed class AiIdempotencyConflictException(string message) : InvalidOperationException(message) { }

public sealed record AiCapabilities(bool Text, bool Image, bool StructuredOutput, bool Streaming, bool Embeddings);
public sealed record AiUsage(long? InputTokens, long? OutputTokens, long? TotalTokens);
public sealed record AiContextItem(string Reference, string Text, string? MediaType = null, byte[]? Data = null);
/// <summary>Server-resolved document/version pair that authorizes an execution.</summary>
public sealed record AiExecutionSource(Guid DocumentId, Guid VersionId);
public sealed record AiRequest(
    Guid TenantId,
    Guid UserId,
    AiTask Task,
    string Instructions,
    IReadOnlyList<AiContextItem> Context,
    JsonDocument? OutputSchema = null,
    string? IdempotencyKey = null,
    IReadOnlyList<AiExecutionSource>? Sources = null,
    Func<CancellationToken, Task>? OnRequestSent = null)
{
    /// <summary>Sources registered server-side. Never trusted from the client.</summary>
    public IReadOnlyList<AiExecutionSource> SourceDocuments => Sources ?? [];
}
public sealed record AiResult(
    bool Success,
    string? Text,
    JsonDocument? StructuredData,
    AiUsage? Usage,
    string Provider,
    string Model,
    AiFailureKind Failure = AiFailureKind.None,
    string? Limitation = null,
    string? CorrelationId = null,
    bool ProviderReached = false);

/// <summary>Server-side gateway. Callers must supply only tenant-authorized context.</summary>
public interface IDocumentAiGateway
{
    Task<AiResult> ExecuteAsync(AiRequest request, CancellationToken cancellationToken);
    IReadOnlyDictionary<string, AiCapabilities> Capabilities { get; }
    /// <summary>Globally configured and clamped maximum output tokens, used to size quota reservations.</summary>
    int MaxOutputTokens { get; }
}

/// <summary>Tasks actually implemented end-to-end (suggestion plus human review). Exposed to administration.</summary>
public static class AiTaskCatalog
{
    public static readonly AiTask[] Supported = [AiTask.Summarize, AiTask.ExtractMetadata, AiTask.SuggestClassification];
    public static bool IsSupported(AiTask task) => task is AiTask.Summarize or AiTask.ExtractMetadata or AiTask.SuggestClassification;
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
    /// <summary>Models for which this adapter/provider combination was explicitly homologated with strict JSON schema.</summary>
    public List<string> StructuredOutputModels { get; set; } = [];
}

public enum AiExecutionState { Reserved, Running, Completed, Rejected, Failed, Cancelled, RemoteOutcomeUnknown, Expired }

public sealed record AiEffectivePolicy(
    Guid TenantId, long Revision, bool Enabled, IReadOnlyCollection<AiTask> Tasks,
    IReadOnlyCollection<string> Providers, IReadOnlyDictionary<AiTask, string> Models,
    long MonthlyTokenLimit, int MaximumInputCharacters, DateTimeOffset PeriodStart,
    long ConsumedTokens, long ReservedTokens);

public sealed record AiExecutionLease(Guid ExecutionId, bool IsOwner, AiExecutionState State, AiResult? ExistingResult = null);
public sealed record AiExecutionStatus(Guid ExecutionId, Guid TenantId, Guid UserId, AiTask Task,
    AiExecutionState State, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, AiResult? Result,
    IReadOnlyList<AiExecutionSource>? Sources = null, DateTimeOffset? ResultExpiresAt = null);

/// <summary>Recovery view over executions whose local or remote outcome still needs attention.</summary>
public sealed record AiRecoveryHealth(int RemoteOutcomeUnknown, int Expired, int PendingExpired);

/// <summary>Persistent tenant policy and distributed execution/quota boundary.</summary>
public interface IAiGovernanceStore
{
    Task<AiEffectivePolicy?> GetEffectivePolicyAsync(Guid tenantId, AiTask task, CancellationToken ct);
    Task<AiExecutionLease> ReserveAsync(AiRequest request, string provider, string model, long policyRevision, long estimatedTokens, CancellationToken ct);
    /// <summary>Atomically confirms that the lease is still valid and its policy revision is current.</summary>
    Task<bool> MarkRunningAsync(Guid executionId, CancellationToken ct);
    /// <summary>Stamps the real send instant. Idempotent; only applies while the execution is Running and unsettled.</summary>
    Task<bool> MarkSentAsync(Guid executionId, CancellationToken ct);
    Task CompleteAsync(Guid executionId, AiResult result, long reservedTokens, TimeSpan duration, CancellationToken ct);
    Task<AiExecutionStatus?> GetExecutionAsync(Guid tenantId, Guid userId, Guid executionId, CancellationToken ct);
    Task<int> ExpireReservationsAsync(CancellationToken ct);
    Task<AiRecoveryHealth> GetRecoveryHealthAsync(CancellationToken ct);
}
