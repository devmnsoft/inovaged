namespace InovaGed.Application.Observability;

public sealed record SliDefinition(string Code, string Name, string Service, string IndicatorType);
public sealed record SliMeasurement(long ValidEvents, long GoodEvents)
{
    public long BadEvents => Math.Max(0, ValidEvents - GoodEvents);
}
public interface ISliCatalog { IReadOnlyCollection<SliDefinition> All { get; } }
public sealed class SliCalculator
{
    public decimal? Calculate(SliMeasurement value) => value.ValidEvents <= 0 ? null : 100m * value.GoodEvents / value.ValidEvents;
}
public enum ErrorBudgetStatus { Healthy, Watch, AtRisk, Exhausted, NotEnoughData }
public sealed record ErrorBudgetResult(decimal Objective, long ValidEvents, long GoodEvents, long BadEvents,
    decimal TotalBudget, decimal ConsumedBudget, decimal RemainingBudget, decimal BurnRate, ErrorBudgetStatus Status);
public interface IErrorBudgetCalculator { ErrorBudgetResult Calculate(decimal objective, SliMeasurement measurement); }
public sealed class ErrorBudgetCalculator : IErrorBudgetCalculator
{
    public ErrorBudgetResult Calculate(decimal objective, SliMeasurement m)
    {
        if (m.ValidEvents <= 0) return new(objective, 0, 0, 0, 0, 0, 0, 0, ErrorBudgetStatus.NotEnoughData);
        var total = m.ValidEvents * (1m - objective / 100m);
        var consumed = m.BadEvents;
        var remaining = Math.Max(0, total - consumed);
        var burn = total <= 0 ? (consumed > 0 ? decimal.MaxValue : 0) : consumed / total;
        var status = remaining == 0 && consumed > 0 ? ErrorBudgetStatus.Exhausted : burn >= .8m ? ErrorBudgetStatus.AtRisk : burn >= .5m ? ErrorBudgetStatus.Watch : ErrorBudgetStatus.Healthy;
        return new(objective, m.ValidEvents, m.GoodEvents, m.BadEvents, total, consumed, remaining, burn, status);
    }
}

public enum IncidentStatus { Detected, Open, Acknowledged, Investigating, Mitigating, Monitoring, Resolved, Closed, Suppressed }
public enum IncidentSeverity { Sev1, Sev2, Sev3, Sev4 }
public static class IncidentLifecycle
{
    private static readonly IReadOnlyDictionary<IncidentStatus, IncidentStatus[]> Allowed = new Dictionary<IncidentStatus, IncidentStatus[]>
    {
        [IncidentStatus.Detected] = [IncidentStatus.Open, IncidentStatus.Suppressed], [IncidentStatus.Open] = [IncidentStatus.Acknowledged, IncidentStatus.Resolved],
        [IncidentStatus.Acknowledged] = [IncidentStatus.Investigating], [IncidentStatus.Investigating] = [IncidentStatus.Mitigating, IncidentStatus.Monitoring],
        [IncidentStatus.Mitigating] = [IncidentStatus.Monitoring], [IncidentStatus.Monitoring] = [IncidentStatus.Resolved], [IncidentStatus.Resolved] = [IncidentStatus.Closed],
        [IncidentStatus.Closed] = [], [IncidentStatus.Suppressed] = [IncidentStatus.Open, IncidentStatus.Closed]
    };
    public static bool CanTransition(IncidentStatus from, IncidentStatus to) => Allowed[from].Contains(to);
}

public sealed record RunbookDefinition(string Code, string Symptom, string Impact, IReadOnlyList<string> Checks, string Mitigation, string Rollback, string Escalation, string Evidence, string ResolutionCriteria);
public interface IRunbookCatalog { RunbookDefinition? Find(string code); }
