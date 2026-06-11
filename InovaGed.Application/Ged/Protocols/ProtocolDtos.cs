using System.Security.Claims;
using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Protocols;

public static class ProtocolStatuses
{
    public const string Requested = "REQUESTED";
    public const string InReview = "IN_REVIEW";
    public const string ReturnedForAdjustment = "RETURNED_FOR_ADJUSTMENT";
    public const string AdjustmentAnswered = "ADJUSTMENT_ANSWERED";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Finished = "FINISHED";
    public const string Cancelled = "CANCELLED";

    public static string Label(string? status) => (status ?? string.Empty).ToUpperInvariant() switch
    {
        Requested => "Solicitado",
        InReview => "Em análise",
        ReturnedForAdjustment => "Devolvido para ajuste",
        AdjustmentAnswered => "Ajuste respondido",
        Approved => "Aprovado",
        Rejected => "Rejeitado",
        Finished => "Finalizado",
        Cancelled => "Cancelado",
        _ => status ?? "-"
    };
}

public sealed class ProtocolVisibilityScope
{
    public bool IsAdmin { get; init; }
    public bool IsAdministradorOphir { get; init; }
    public bool IsArquivistaOphir { get; init; }
    public Guid? SectorId { get; init; }
    public string? SectorName { get; init; }
    public bool CanManage => IsAdmin || IsAdministradorOphir;
}

public sealed class ProtocolRequestCreateVm
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = "NORMAL";
    public Guid? AssignedSectorId { get; set; }
    public string? AssignedSectorName { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public List<Guid> DocumentIds { get; set; } = new();
    public List<ProtocolManualItemVm> ManualItems { get; set; } = new();
    public int PendingAttachmentsCount { get; set; }
    public Guid? PreselectedDocumentId { get; set; }
}

public sealed class ProtocolManualItemVm
{
    public string? ReferenceCode { get; set; }
    public string? Description { get; set; }
    public string? DocumentType { get; set; }
    public string? PatientName { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? BoxCode { get; set; }
    public string? PhysicalLocation { get; set; }
    public string? Notes { get; set; }
}

public sealed class ProtocolWorkQueueFilter
{
    public string? Q { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public Guid? SectorId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public bool OnlyMine { get; set; }
    public bool Overdue { get; set; }
    public bool ReturnedForAdjustment { get; set; }
}

public sealed class ProtocolRequestRowVm
{
    public Guid Id { get; set; }
    public string ProtocolNo { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusLabel => ProtocolStatuses.Label(Status);
    public string Priority { get; set; } = string.Empty;
    public string? RequesterName { get; set; }
    public string? RequesterSectorName { get; set; }
    public string? AssignedSectorName { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public int ItemsCount { get; set; }
    public int AttachmentsCount { get; set; }
    public bool IsOverdue { get; set; }
}

public sealed class ProtocolWorkQueueVm
{
    public ProtocolWorkQueueFilter Filter { get; set; } = new();
    public List<ProtocolRequestRowVm> Rows { get; set; } = new();
}

public sealed class ProtocolRequestDetailsVm
{
    public ProtocolRequestRowVm Header { get; set; } = new();
    public string? Description { get; set; }
    public List<ProtocolItemVm> Items { get; set; } = new();
    public List<ProtocolAttachmentVm> Attachments { get; set; } = new();
    public List<ProtocolHistoryVm> History { get; set; } = new();
    public List<ProtocolLoanVm> Loans { get; set; } = new();
    public bool HasDocumentsWithoutOcr => Items.Any(i => i.DocumentId.HasValue && !i.HasOcr);
}

public sealed class ProtocolItemVm
{
    public Guid Id { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? DocumentVersionId { get; set; }
    public bool IsManual { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Description { get; set; }
    public string? DocumentType { get; set; }
    public string? PatientName { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? BoxCode { get; set; }
    public string? PhysicalLocation { get; set; }
    public string? Notes { get; set; }
    public string? DocumentCode { get; set; }
    public string? DocumentTitle { get; set; }
    public string? OcrStatus { get; set; }
    public string? Classification { get; set; }
    public int? PartialPartNumber { get; set; }
    public int? PartialTotalParts { get; set; }
    public bool HasOcr { get; set; }
}

public sealed class ProtocolAttachmentVm
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public string? UploadedByName { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class ProtocolHistoryVm
{
    public DateTimeOffset CreatedAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? UserName { get; set; }
    public string? SectorName { get; set; }
    public string? Reason { get; set; }
    public string? InternalNotes { get; set; }
}

public sealed class ProtocolLoanVm
{
    public Guid Id { get; set; }
    public long ProtocolNo { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class ProtocolDocumentPickDto
{
    public Guid Id { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? OcrStatus { get; set; }
    public string? Classification { get; set; }
    public bool HasOcr { get; set; }
}

public interface IProtocolAccessService
{
    Task<ProtocolVisibilityScope> BuildScopeAsync(Guid tenantId, Guid? userId, ClaimsPrincipal user, CancellationToken ct);
    Task<bool> CanViewAsync(Guid tenantId, Guid protocolRequestId, Guid? userId, ClaimsPrincipal user, CancellationToken ct);
    Task<bool> CanManageAsync(Guid tenantId, Guid protocolRequestId, Guid? userId, ClaimsPrincipal user, CancellationToken ct);
}

public interface IProtocolQueryService
{
    Task<IReadOnlyList<ProtocolRequestRowVm>> ListMyAsync(Guid tenantId, Guid userId, ProtocolVisibilityScope scope, ProtocolWorkQueueFilter filter, CancellationToken ct);
    Task<IReadOnlyList<ProtocolRequestRowVm>> ListWorkQueueAsync(Guid tenantId, Guid userId, ProtocolVisibilityScope scope, ProtocolWorkQueueFilter filter, CancellationToken ct);
    Task<ProtocolRequestDetailsVm?> GetDetailsAsync(Guid tenantId, Guid id, Guid userId, ProtocolVisibilityScope scope, CancellationToken ct);
    Task<IReadOnlyList<ProtocolDocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string q, CancellationToken ct);
}

public interface IProtocolCommandService
{
    Task<Result<Guid>> CreateAsync(Guid tenantId, Guid userId, ProtocolRequestCreateVm vm, CancellationToken ct);
    Task<Result> AssumeAsync(Guid tenantId, Guid id, Guid userId, string? notes, CancellationToken ct);
    Task<Result> ApproveAsync(Guid tenantId, Guid id, Guid userId, string reason, string? internalNotes, CancellationToken ct);
    Task<Result> ReturnForAdjustmentAsync(Guid tenantId, Guid id, Guid userId, string reason, string? internalNotes, CancellationToken ct);
    Task<Result> RejectAsync(Guid tenantId, Guid id, Guid userId, string reason, string? internalNotes, CancellationToken ct);
    Task<Result> FinishAsync(Guid tenantId, Guid id, Guid userId, string reason, string? internalNotes, CancellationToken ct);
    Task<Result> RespondAdjustmentAsync(Guid tenantId, Guid id, Guid userId, string response, CancellationToken ct);
    Task<Result> AddAttachmentAsync(Guid tenantId, Guid id, Guid userId, string fileName, string? contentType, long sizeBytes, string storagePath, CancellationToken ct);
    Task<Result<Guid>> CreateLoanAsync(Guid tenantId, Guid id, Guid userId, CancellationToken ct);
}

public interface IProtocolRequestService : IProtocolQueryService, IProtocolCommandService { }

public interface IProtocolHistoryWriter
{
    Task WriteAsync(Guid tenantId, Guid protocolRequestId, string action, string? oldStatus, string? newStatus, Guid? userId, string? userName, Guid? sectorId, string? sectorName, string? reason, string? internalNotes, object? metadata, string? correlationId, CancellationToken ct);
}
