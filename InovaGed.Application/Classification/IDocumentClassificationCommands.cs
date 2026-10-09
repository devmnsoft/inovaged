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

public enum ClassificationEditAction
{
    Keep = 0,
    Replace = 1,
    Remove = 2
}

public enum DocumentTypeEditAction
{
    Keep = 0,
    Replace = 1,
    Remove = 2
}

public sealed record SaveManualIntegratedCommand(
    Guid TenantId,
    Guid DocumentId,
    Guid? UserId,
    ClassificationEditAction ClassificationAction = ClassificationEditAction.Keep,
    Guid? ClassificationId = null,
    bool ConfirmClassificationRemoval = false,
    DocumentTypeEditAction TypeAction = DocumentTypeEditAction.Keep,
    Guid? DocumentTypeId = null,
    bool HasTags = false,
    IReadOnlyList<string>? Tags = null,
    bool HasMetadata = false,
    IReadOnlyDictionary<string, string>? Metadata = null,
    bool IsSystemExecution = false,
    string? SystemExecutionReason = null
)
{
    public SaveManualIntegratedCommand(
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
    ) : this(
        TenantId,
        DocumentId,
        UserId,
        ClassificationAction: !HasClassification ? ClassificationEditAction.Keep : (ClassificationId.HasValue && ClassificationId.Value != Guid.Empty ? ClassificationEditAction.Replace : ClassificationEditAction.Remove),
        ClassificationId: ClassificationId,
        ConfirmClassificationRemoval: true,
        TypeAction: !HasDocumentType ? DocumentTypeEditAction.Keep : (DocumentTypeId.HasValue && DocumentTypeId.Value != Guid.Empty ? DocumentTypeEditAction.Replace : DocumentTypeEditAction.Remove),
        DocumentTypeId: DocumentTypeId,
        HasTags: HasTags,
        Tags: Tags,
        HasMetadata: HasMetadata,
        Metadata: Metadata,
        IsSystemExecution: false,
        SystemExecutionReason: null
    )
    {
    }

    public bool HasClassification => ClassificationAction != ClassificationEditAction.Keep;
    public bool HasDocumentType => TypeAction != DocumentTypeEditAction.Keep;
}

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
