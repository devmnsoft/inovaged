namespace InovaGed.Application.Notifications;

public interface INotificationSender
{
    Task SendAsync(string title, string message, CancellationToken ct);
}