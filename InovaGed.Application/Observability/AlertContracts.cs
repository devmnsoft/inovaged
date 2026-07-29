namespace InovaGed.Application.Observability;

public sealed record AlertContext(string Code, string Service, string Cluster, string Environment, decimal Value, DateTimeOffset ObservedAt);
public sealed record AlertDecision(bool Firing, string DeduplicationKey, string? RunbookCode);
public interface IAlertRule { AlertDecision Evaluate(AlertContext context); }
public interface IAlertEvaluator { AlertDecision Evaluate(IAlertRule rule, AlertContext context); }
public interface IAlertDispatcher { Task DispatchAsync(AlertDecision decision, AlertContext context, CancellationToken cancellationToken); }
public interface IAlertNotificationSink { Task NotifyAsync(AlertDecision decision, AlertContext context, CancellationToken cancellationToken); }
public static class AlertDeduplication
{
    public static string Key(AlertContext value) => string.Join(':', value.Code, value.Service, value.Cluster, value.Environment).ToLowerInvariant();
}
