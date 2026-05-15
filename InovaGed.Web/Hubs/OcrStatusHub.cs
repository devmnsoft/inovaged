using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace InovaGed.Web.Hubs;

[Authorize]
public sealed class OcrStatusHub : Hub
{
    public const string Route = "/hubs/ocr-status";

    public Task SubscribeTenant(string tenantId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");

    public Task SubscribeVersion(string versionId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"version:{versionId}");
}
