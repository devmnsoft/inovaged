namespace InovaGed.Application.Pacs;

public interface IOcrQueue
{
    Task EnqueuePacsAsync(
        Guid tenantId,
        Guid ticketId,
        Guid ticketFileId,
        string storageRelPath,
        CancellationToken ct);
}