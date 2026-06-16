using System.Security.Claims;

namespace InovaGed.Application.Ged.Documents;

public sealed class BulkDocumentActionRequest
{
    public IReadOnlyList<Guid> DocumentIds { get; set; } = Array.Empty<Guid>();
    public string? Reason { get; set; }
    public Guid? DestinationFolderId { get; set; }
}

public sealed class BulkDocumentActionResponse
{
    public bool Success { get; set; }
    public int Requested { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<BulkDocumentActionItemResult> Items { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public sealed class BulkDocumentActionItemResult
{
    public Guid DocumentId { get; set; }
    public string? Title { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public interface IGedBulkDocumentActionService
{
    Task<BulkDocumentActionResponse> DeleteAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, BulkDocumentActionRequest request, CancellationToken ct);
    Task<BulkDocumentActionResponse> MarkIncompleteAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, BulkDocumentActionRequest request, CancellationToken ct);
    Task<BulkDocumentActionResponse> MarkCompleteAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, BulkDocumentActionRequest request, CancellationToken ct);
}
