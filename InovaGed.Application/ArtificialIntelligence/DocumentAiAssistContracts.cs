namespace InovaGed.Application.ArtificialIntelligence;

public sealed record AssistCaller(Guid TenantId, Guid UserId, bool Authenticated, string? Ip, string? UserAgent);
public sealed record AssistResponse(int StatusCode, object? Body, bool Denied = false);

public sealed record ApplyMetadataCommand(Guid DocumentId, Guid VersionId, Guid ExecutionId, long ConcurrencyToken, bool TitleSet, string? Title, bool DescriptionSet, string? Description, bool IsConfidentialSet, bool? IsConfidential, string? ConfidentialityJustification);
public sealed record ApplyCatalogCommand(Guid DocumentId, Guid VersionId, Guid ExecutionId, long ConcurrencyToken, Guid? SelectedId);

public interface IDocumentAiAssistService
{
    Task<AssistResponse> SummarizeAsync(Guid versionId, string idempotencyKey, AssistCaller caller, CancellationToken ct);
    Task<AssistResponse> GetExecutionAsync(Guid executionId, Guid? versionId, AssistCaller caller, CancellationToken ct);
    Task<AssistResponse> SuggestMetadataAsync(Guid versionId, string idempotencyKey, AssistCaller caller, CancellationToken ct);
    Task<AssistResponse> ApplyMetadataAsync(ApplyMetadataCommand command, AssistCaller caller, CancellationToken ct);
    Task<AssistResponse> SuggestDocumentTypeAsync(Guid versionId, string idempotencyKey, AssistCaller caller, CancellationToken ct);
    Task<AssistResponse> ApplyDocumentTypeAsync(ApplyCatalogCommand command, AssistCaller caller, CancellationToken ct);
    Task<AssistResponse> SuggestArchivalClassAsync(Guid versionId, string idempotencyKey, AssistCaller caller, CancellationToken ct);
    Task<AssistResponse> ApplyArchivalClassAsync(ApplyCatalogCommand command, AssistCaller caller, CancellationToken ct);
}
