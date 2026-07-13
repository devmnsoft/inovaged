namespace InovaGed.Application.Sla;
public enum DocumentSlaState { OnTime, NearDue, Overdue, Suspended, Completed }
public sealed record DocumentSlaPolicy(string TaskType, string? Sector, string Priority, TimeSpan DueIn, TimeSpan NearDueThreshold);
public sealed class DocumentSlaCalculator
{
    public DocumentSlaState Calculate(DateTime createdAtUtc, DateTime nowUtc, DocumentSlaPolicy policy, bool suspended, bool completed)
    {
        if (completed) return DocumentSlaState.Completed;
        if (suspended) return DocumentSlaState.Suspended;
        var due = createdAtUtc.Add(policy.DueIn);
        if (nowUtc > due) return DocumentSlaState.Overdue;
        return due - nowUtc <= policy.NearDueThreshold ? DocumentSlaState.NearDue : DocumentSlaState.OnTime;
    }
}
