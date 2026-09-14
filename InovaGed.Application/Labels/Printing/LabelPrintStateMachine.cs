namespace InovaGed.Application.Labels.Printing;

/// <summary>Single authority for print-job lifecycle transitions.</summary>
public sealed class LabelPrintStateMachine
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [LabelPrintJobStatus.Pending] = Set(LabelPrintJobStatus.Previewed, LabelPrintJobStatus.Cancelled, LabelPrintJobStatus.Error),
            [LabelPrintJobStatus.Previewed] = Set(LabelPrintJobStatus.ReadyToPrint, LabelPrintJobStatus.Cancelled, LabelPrintJobStatus.Error),
            [LabelPrintJobStatus.ReadyToPrint] = Set(LabelPrintJobStatus.PdfGenerated, LabelPrintJobStatus.Cancelled, LabelPrintJobStatus.Error),
            [LabelPrintJobStatus.PdfGenerated] = Set(LabelPrintJobStatus.Printed, LabelPrintJobStatus.Cancelled, LabelPrintJobStatus.Error),
            [LabelPrintJobStatus.Error] = Set(LabelPrintJobStatus.Pending)
        };

    public bool CanTransition(string current, string next) =>
        Transitions.TryGetValue(current, out var allowed) && allowed.Contains(next);

    public void EnsureTransition(string current, string next)
    {
        if (!CanTransition(current, next))
            throw new InvalidOperationException($"Transição de impressão inválida: {current} → {next}.");
    }

    public bool CanCancel(string status) => CanTransition(status, LabelPrintJobStatus.Cancelled);
    public bool CanRetry(string status) => status == LabelPrintJobStatus.Error;
    public bool CanMarkPrinted(string status) => status == LabelPrintJobStatus.PdfGenerated;
    public bool IsTerminal(string status) => status is LabelPrintJobStatus.Printed or LabelPrintJobStatus.Cancelled;

    private static IReadOnlySet<string> Set(params string[] values) => values.ToHashSet(StringComparer.Ordinal);
}
