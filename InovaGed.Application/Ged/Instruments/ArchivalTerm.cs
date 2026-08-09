namespace InovaGed.Application.Ged.Instruments;

/// <summary>Interpreta prazos arquivísticos sem perder a redação normativa original.</summary>
public sealed record ArchivalTerm(string RawValue, string? Event)
{
    public const string WaitingEvent = "AGUARDANDO_EVENTO";
    public const string RequiresReview = "REGRA_REQUER_REVISAO";

    public string Evaluate(bool eventOccurred)
    {
        var value = RawValue?.Trim() ?? string.Empty;
        if (value.Length == 0 || value is "*" ||
            value.Contains("vigência", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("último registro", StringComparison.OrdinalIgnoreCase))
            return RequiresReview;
        if (!string.IsNullOrWhiteSpace(Event) && !eventOccurred)
            return WaitingEvent;
        return value;
    }
}
