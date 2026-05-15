using InovaGed.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace InovaGed.Web.ocr;

public interface IOcrSignalRNotifier
{
    Task PublishOcrStatusAsync(Guid tenantId, Guid versionId, string status, long? jobId, string? message, CancellationToken ct);
}

public sealed class OcrSignalRNotifier : IOcrSignalRNotifier
{
    private readonly IHubContext<OcrStatusHub> _hub;

    public OcrSignalRNotifier(IHubContext<OcrStatusHub> hub) => _hub = hub;

    public Task PublishOcrStatusAsync(Guid tenantId, Guid versionId, string status, long? jobId, string? message, CancellationToken ct)
    {
        var payload = new
        {
            tenantId,
            versionId,
            status,
            jobId,
            message,
            at = DateTimeOffset.UtcNow
        };

        return Task.WhenAll(
            _hub.Clients.Group($"tenant:{tenantId}").SendAsync("ocr.status", payload, ct),
            _hub.Clients.Group($"version:{versionId}").SendAsync("ocr.status", payload, ct));
    }
}
