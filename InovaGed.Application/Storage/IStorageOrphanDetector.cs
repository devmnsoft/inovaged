namespace InovaGed.Application.Storage;

public sealed record StorageOrphanReport(
    DateTimeOffset GeneratedAt,
    TimeSpan GracePeriod,
    int TotalFilesScanned,
    int ConfirmedReferencedFiles,
    int ActiveUploadFiles,
    int OrphanCandidatesFound,
    long OrphanBytesTotal,
    IReadOnlyList<OrphanFileInfo> Candidates);

public sealed record OrphanFileInfo(
    string RelativePath,
    long SizeBytes,
    DateTimeOffset LastModified,
    string Reason);

public interface IStorageOrphanDetector
{
    Task<StorageOrphanReport> ScanOrphansAsync(Guid? tenantId, TimeSpan gracePeriod, CancellationToken ct);
}
