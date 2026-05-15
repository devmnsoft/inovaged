namespace InovaGed.Application.Preview;
public interface IPreviewJobQueue
{
    Task EnqueueAsync(Guid tenantId, Guid documentId, Guid versionId, string storagePath, string fileName, CancellationToken ct);
}
