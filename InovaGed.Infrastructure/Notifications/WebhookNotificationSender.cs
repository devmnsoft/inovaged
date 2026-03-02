using System.Text;
using InovaGed.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Notifications;

public sealed class WebhookNotificationSender : INotificationSender
{
    private readonly HttpClient _http;
    private readonly ILogger<WebhookNotificationSender> _logger;
    private readonly string? _url;

    public WebhookNotificationSender(HttpClient http, IConfiguration cfg, ILogger<WebhookNotificationSender> logger)
    {
        _http = http;
        _logger = logger;
        _url = cfg["Notifications:WebhookUrl"];
    }

    public async Task SendAsync(string title, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_url))
        {
            _logger.LogInformation("WebhookUrl not configured. Title={Title} Msg={Msg}", title, message);
            return;
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new { title, message });
        var res = await _http.PostAsync(_url, new StringContent(payload, Encoding.UTF8, "application/json"), ct);

        if (!res.IsSuccessStatusCode)
            _logger.LogWarning("Webhook notify failed. Status={Status}", (int)res.StatusCode);
    }
}