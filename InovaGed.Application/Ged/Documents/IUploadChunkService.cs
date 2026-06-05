using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Documents;

public sealed class StartUploadChunkRequestDto
{
    public Guid? BatchId { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? RequestedFolderId { get; set; }
    public string? FolderName { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long TotalSizeBytes { get; set; }
    public int? ChunkSizeBytes { get; set; }
    public int? TotalChunks { get; set; }
    public int FileIndex { get; set; }
    public int TotalFiles { get; set; }
    public string? DuplicateStrategy { get; set; }
    public bool RunOcr { get; set; }
    public bool GeneratePreview { get; set; }
    public Guid? ExistingDocumentId { get; set; }
    public string? UploadName { get; set; }
    public DocumentBulkUploadMetadata Metadata { get; set; } = new();
    public string? CorrelationId { get; set; }
}

public sealed class UploadChunkSessionDto
{
    public Guid UploadId { get; set; }
    public Guid? BatchId { get; set; }
    public int ChunkSizeBytes { get; set; }
    public int TotalChunks { get; set; }
    public int NextChunk { get; set; }
    public IReadOnlyList<int> ReceivedChunks { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> MissingChunks { get; set; } = Array.Empty<int>();
    public string Status { get; set; } = "OPEN";
    public double Percent { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class UploadChunkPartRequestDto
{
    public Guid UploadId { get; set; }
    public int ChunkIndex { get; set; }
    public Stream Content { get; set; } = Stream.Null;
    public long SizeBytes { get; set; }
    public string? ChecksumSha256 { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class UploadChunkStatusDto
{
    public Guid UploadId { get; set; }
    public Guid? BatchId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public int ReceivedChunksCount { get; set; }
    public IReadOnlyList<int> ReceivedChunks { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> MissingChunks { get; set; } = Array.Empty<int>();
    public double Percent { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IUploadChunkService
{
    Task<Result<UploadChunkSessionDto>> StartAsync(Guid tenantId, Guid userId, bool isAdmin, string? userName, StartUploadChunkRequestDto request, CancellationToken ct);
    Task<Result<UploadChunkStatusDto>> SavePartAsync(Guid tenantId, Guid userId, UploadChunkPartRequestDto request, CancellationToken ct);
    Task<Result<UploadBatchFileResultDto>> CompleteAsync(Guid tenantId, Guid userId, Guid uploadId, CancellationToken ct);
    Task<Result<UploadChunkStatusDto>> GetStatusAsync(Guid tenantId, Guid userId, Guid uploadId, CancellationToken ct);
    Task<Result<UploadChunkStatusDto>> CancelAsync(Guid tenantId, Guid userId, Guid uploadId, CancellationToken ct);
}
