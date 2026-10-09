using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InovaGed.Application.Classification;

public interface IDocumentClassificationCommands
{
    Task SaveManualAsync(
        Guid tenantId,
        Guid documentId,
        Guid? documentTypeId,
        Guid? userId,
        IReadOnlyList<string>? tags,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct);

    Task ApplySuggestionAsync(
        Guid tenantId,
        Guid documentId,
        Guid suggestedTypeId,
        decimal? suggestedConfidence,
        string? suggestedSummary,
        Guid? userId,
        CancellationToken ct);

    // ✅ ADICIONE
    Task SaveSuggestionOnlyAsync(
        Guid tenantId,
        Guid documentId,
        Guid suggestedTypeId,
        decimal? suggestedConfidence,
        string? suggestedSummary,
        CancellationToken ct);

    Task<SaveManualIntegratedResult> SaveManualIntegratedAsync(
        SaveManualIntegratedCommand command,
        CancellationToken ct);
}

public sealed record SaveManualIntegratedCommand(
    Guid TenantId,
    Guid DocumentId,
    Guid? UserId,
    bool HasClassification,
    Guid? ClassificationId,
    bool HasDocumentType,
    Guid? DocumentTypeId,
    bool HasTags,
    IReadOnlyList<string>? Tags,
    bool HasMetadata,
    IReadOnlyDictionary<string, string>? Metadata
);

public sealed record SaveManualIntegratedResult(
    bool Success,
    bool ClassificationChanged,
    bool TypeChanged,
    bool TagsChanged,
    bool MetadataChanged,
    bool RetentionRecalculated,
    string Message,
    string? ErrorCode = null
);
