using InovaGed.Application.Preview;
using InovaGed.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
namespace InovaGed.Infrastructure.Preview;
public sealed class PreviewSignalRNotifier : IPreviewSignalRNotifier
{
    private readonly IHubContext<OcrStatusHub> _hub;
    public PreviewSignalRNotifier(IHubContext<OcrStatusHub> hub) => _hub = hub;
    public Task PublishAsync(Guid tenantId, Guid versionId, string status, string? previewUrl, string? message, CancellationToken ct)
        => _hub.Clients.Group($"tenant:{tenantId}").SendAsync("previewStatus", new { tenantId, versionId, status, previewUrl, message, ts = DateTimeOffset.UtcNow }, ct);
}
