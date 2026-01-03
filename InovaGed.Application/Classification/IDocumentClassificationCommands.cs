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
        IReadOnlyList<string> tags,
        IReadOnlyDictionary<string, string> metadata,
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
}
