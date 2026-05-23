using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Documents;

public sealed class DocumentMoveRequestVM { public Guid DocumentId { get; set; } public Guid DestinationFolderId { get; set; } public string? Reason { get; set; } public string? Source { get; set; } }
public sealed class DocumentBulkMoveRequestVM { public List<Guid> DocumentIds { get; set; } = new(); public Guid DestinationFolderId { get; set; } public string? Reason { get; set; } public string? Source { get; set; } }
public sealed class DocumentMoveResultDto { public Guid DocumentId { get; set; } public bool Success { get; set; } public string? Message { get; set; } public string? ErrorCode { get; set; } public Guid? OldFolderId { get; set; } public Guid? NewFolderId { get; set; } }
public sealed class DocumentBulkMoveResultDto { public Guid BatchId { get; set; } public int Total { get; set; } public int SuccessCount { get; set; } public int FailCount { get; set; } public IReadOnlyList<DocumentMoveResultDto> Items { get; set; } = Array.Empty<DocumentMoveResultDto>(); }
public sealed class FolderOptionDto { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string? FullPath { get; set; } public Guid? ParentId { get; set; } }
public sealed class DocumentMoveHistoryDto { public Guid Id { get; set; } public Guid DocumentId { get; set; } public Guid? OldFolderId { get; set; } public Guid NewFolderId { get; set; } public string? OldFolderName { get; set; } public string? NewFolderName { get; set; } public Guid MovedBy { get; set; } public string? MovedByName { get; set; } public DateTime MovedAt { get; set; } public string? Reason { get; set; } public string? Source { get; set; } }
