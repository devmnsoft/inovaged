using InovaGed.Application.Common.Notifications;
using InovaGed.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace InovaGed.Web.Notifications;

public sealed class SignalRPreviewNotificationService : IPreviewNotificationService
{
    private readonly IHubContext<OcrStatusHub> _hub;

    public SignalRPreviewNotificationService(IHubContext<OcrStatusHub> hub)
        => _hub = hub;

    public Task PublishAsync(
        Guid tenantId,
        Guid versionId,
        string status,
        string? previewUrl,
        string? message,
        CancellationToken ct)
        => _hub.Clients
            .Group($"tenant:{tenantId}")
            .SendAsync(
                "previewStatus",
                new
                {
                    tenantId,
                    versionId,
                    status,
                    previewUrl,
                    message,
                    ts = DateTimeOffset.UtcNow
                },
                ct);
}
