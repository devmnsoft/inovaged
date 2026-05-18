namespace InovaGed.Application.Common.Notifications;

public interface IPreviewNotificationService
{
    Task PublishAsync(
        Guid tenantId,
        Guid versionId,
        string status,
        string? previewUrl,
        string? message,
        CancellationToken ct);
}
