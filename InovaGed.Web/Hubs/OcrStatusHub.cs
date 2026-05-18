using Microsoft.AspNetCore.SignalR;

namespace InovaGed.Web.Hubs;

public sealed class OcrStatusHub : Hub
{
    public const string Route = "/hubs/ocr-status";

    public Task JoinTenant(Guid tenantId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");

    public Task LeaveTenant(Guid tenantId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
}
