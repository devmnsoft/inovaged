namespace InovaGed.Application.Preview;
public interface IPreviewSignalRNotifier
{
    Task PublishAsync(Guid tenantId, Guid versionId, string status, string? previewUrl, string? message, CancellationToken ct);
}
