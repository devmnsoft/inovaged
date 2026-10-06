namespace InovaGed.Application.ArtificialIntelligence;

/// <summary>Review recorded with the canonical write. The browser does not replace the stored suggestion.</summary>
public sealed record AssistedApplicationRecord(
    Guid TenantId,
    Guid ExecutionId,
    Guid DocumentId,
    Guid VersionId,
    string Task,
    Guid ReviewerId,
    string DecisionFingerprint,
    string DecisionJson,
    string Outcome,
    bool Partial,
    string AuditAction,
    string AuditMessage,
    string AuditDetailsJson,
    string? Ip,
    string? UserAgent,
    string? CorrelationId,
    bool QueueRetention = false,
    string? OperationKey = null);

public sealed record AssistedWriteResult(string Code, string? Message, long? ConcurrencyToken, Guid? ApplicationId, bool Partial = false, bool RetentionPending = false, Guid? PendingId = null, string? Outcome = null, int RetentionAttempts = 0);

public sealed record StoredReview(Guid Id, string Fingerprint, string DecisionJson, string Outcome, bool Partial, bool RetentionPending, int RetentionAttempts, DateTimeOffset? RetentionResolvedAt);

public sealed record RetentionClaim(Guid Id, Guid DocumentId, Guid? ApplicationId, int Attempts);

public sealed record RetentionPendingItem(Guid Id, Guid DocumentId, DateTimeOffset CreatedAt, string State, int Attempts, string? LastError, DateTimeOffset? ResolvedAt);

public sealed class ReviewHistoryRow
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string Kind { get; set; } = "";
    public string Task { get; set; } = "";
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public Guid? ReviewerId { get; set; }
    public string? Outcome { get; set; }
    public bool Partial { get; set; }
    public string? DecisionJson { get; set; }
    public DateTimeOffset? ResultExpiresAt { get; set; }
    public string? ExecutionState { get; set; }
    public Guid? PendingId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public long Total { get; set; }
}

public interface IAssistedDocumentStore
{
    Task<AssistedWriteResult> ApplyMetadataAsync(AssistedApplicationRecord application, long concurrencyToken, string title, string? description, bool confidential, bool mutate, CancellationToken ct);
    Task<AssistedWriteResult> ApplyDocumentTypeAsync(AssistedApplicationRecord application, long concurrencyToken, Guid typeId, bool mutate, CancellationToken ct);
    Task<AssistedWriteResult> ApplyArchivalClassAsync(AssistedApplicationRecord application, long concurrencyToken, Guid classificationId, bool mutate, CancellationToken ct);
    Task<StoredReview?> FindReviewAsync(Guid tenantId, string operationKey, CancellationToken ct);
    Task<RetentionClaim?> ClaimRetentionAsync(Guid tenantId, Guid pendingId, Guid claimToken, CancellationToken ct);
    Task<bool> ResolveRetentionAsync(Guid tenantId, Guid pendingId, Guid claimToken, CancellationToken ct);
    Task FailRetentionAsync(Guid tenantId, Guid pendingId, Guid claimToken, string error, CancellationToken ct);
    Task<IReadOnlyList<ReviewHistoryRow>> ListReviewsAsync(Guid tenantId, Guid documentId, int offset, int limit, CancellationToken ct);
    Task<IReadOnlyList<RetentionPendingItem>> ListRetentionAsync(Guid tenantId, Guid documentId, bool includeResolved, int offset, int limit, CancellationToken ct);
    Task<RetentionPendingItem?> GetRetentionAsync(Guid tenantId, Guid pendingId, CancellationToken ct);
}

public enum AiSourceIntegrity { Resolved, LegacyFormat, Missing, Corrupted }

public sealed record AiSourceRead(AiSourceIntegrity Integrity, IReadOnlyList<AiExecutionSource> Sources);

/// <summary>
/// Reads execution sources without a permissive fallback.
/// Invalid JSON is corrupted. A valid legacy reference list is only a candidate until the caller proves each version exists.
/// </summary>
public static class AiExecutionSourceCodec
{
    public static AiSourceRead Read(string? sourceDocumentsJson, string? documentRefsJson)
    {
        var sources = ParseSourceArray(sourceDocumentsJson, out var sourceState);
        if (sourceState == ParseState.Corrupted) return new(AiSourceIntegrity.Corrupted, []);
        if (sourceState == ParseState.Values) return new(AiSourceIntegrity.Resolved, sources);
        var legacy = ParseLegacyRefs(documentRefsJson, out var legacyState);
        if (legacyState == ParseState.Corrupted) return new(AiSourceIntegrity.Corrupted, []);
        if (legacyState == ParseState.Values) return new(AiSourceIntegrity.LegacyFormat, legacy);
        return new(AiSourceIntegrity.Missing, []);
    }

    private enum ParseState { Empty, Values, Corrupted }

    private static IReadOnlyList<AiExecutionSource> ParseSourceArray(string? json, out ParseState state)
    {
        state = ParseState.Empty;
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) { state = ParseState.Corrupted; return []; }
            if (document.RootElement.GetArrayLength() == 0) return [];
            var list = new List<AiExecutionSource>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !TryIdentifier(item, "documentId", out var documentGuid)
                    || !TryIdentifier(item, "versionId", out var versionGuid)
                    || documentGuid == versionGuid
                    || list.Any(x => x.VersionId == versionGuid && x.DocumentId != documentGuid))
                { state = ParseState.Corrupted; return []; }
                list.Add(new AiExecutionSource(documentGuid, versionGuid));
            }
            state = ParseState.Values;
            return list;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { state = ParseState.Corrupted; return []; }
    }

    private static bool TryIdentifier(System.Text.Json.JsonElement item, string name, out Guid value)
    {
        value = Guid.Empty;
        if (!item.TryGetProperty(name, out var element) || element.ValueKind != System.Text.Json.JsonValueKind.String) return false;
        var text = element.GetString();
        return !string.IsNullOrWhiteSpace(text) && Guid.TryParse(text, out value) && value != Guid.Empty;
    }

    private static IReadOnlyList<AiExecutionSource> ParseLegacyRefs(string? json, out ParseState state)
    {
        state = ParseState.Empty;
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) { state = ParseState.Corrupted; return []; }
            if (document.RootElement.GetArrayLength() == 0) return [];
            var list = new List<AiExecutionSource>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.String || !TryLegacyReference(item.GetString(), out var source) || source.DocumentId == source.VersionId || list.Any(x => x.VersionId == source.VersionId && x.DocumentId != source.DocumentId))
                { state = ParseState.Corrupted; return []; }
                list.Add(source);
            }
            state = ParseState.Values;
            return list;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { state = ParseState.Corrupted; return []; }
    }

    public static bool TryLegacyReference(string? text, out AiExecutionSource source)
    {
        source = new(Guid.Empty, Guid.Empty);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('/');
        if (parts.Length < 2 || !Guid.TryParseExact(parts[0], "N", out var documentId) || !Guid.TryParseExact(parts[1], "N", out var versionId)) return false;
        source = new(documentId, versionId);
        return true;
    }
}

/// <summary>Selection is not the value. A generic edit permission cannot change secrecy.</summary>
public static class ConfidentialityDecision
{
    public sealed record Outcome(bool Selected, bool? Proposed, bool Changed, bool? Applied, string? Error);

    public static Outcome Resolve(bool current, bool selected, bool? proposed, bool canChangeSecrecy)
    {
        if (!selected) return new(false, proposed, false, current, null);
        if (proposed is null) return new(true, null, false, null, "Informe o valor de sigilo escolhido pelo revisor.");
        if (proposed.Value == current) return new(true, proposed, false, current, null);
        if (!canChangeSecrecy) return new(true, proposed, false, null, "A alteração de sigilo exige a permissão específica de gestão de sigilo. A permissão genérica de edição não autoriza essa mudança.");
        return new(true, proposed, true, proposed.Value, null);
    }
}
