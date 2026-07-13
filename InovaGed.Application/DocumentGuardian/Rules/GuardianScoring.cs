namespace InovaGed.Application.DocumentGuardian.Rules;

public sealed record GuardianFindingInput(string RuleCode, string Severity, string Category, string Status, DateTime CreatedAtUtc, bool SensitiveContext = false, int Recurrence = 0);
public sealed record GuardianScoreResult(decimal Score, string Formula, IReadOnlyList<string> Factors, DateTime CalculatedAtUtc, string Version);

public sealed class GuardianRiskScoreCalculator
{
    public const string Version = "RISK-DET-2026-07";
    public GuardianScoreResult Calculate(IEnumerable<GuardianFindingInput> findings, DateTime nowUtc)
    {
        decimal total = 0; var factors = new List<string>();
        foreach (var f in findings.Where(x => !string.Equals(x.Status, "RESOLVED", StringComparison.OrdinalIgnoreCase)))
        {
            var sev = f.Severity.ToUpperInvariant() switch { "CRITICAL" => 25m, "HIGH" => 15m, "MEDIUM" => 8m, _ => 3m };
            var age = Math.Min(10, Math.Max(0, (nowUtc - f.CreatedAtUtc).TotalDays / 7));
            var sensitive = f.SensitiveContext ? 8 : 0;
            var recurrence = Math.Min(10, f.Recurrence * 2);
            var decision = f.Status.Equals("CONFIRMED", StringComparison.OrdinalIgnoreCase) ? 5 : f.Status.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) ? -8 : 0;
            var subtotal = sev + (decimal)age + sensitive + recurrence + decision;
            total += subtotal; factors.Add($"{f.RuleCode}:{subtotal:0.##}");
        }
        return new GuardianScoreResult(Math.Clamp(total, 0, 100), "min(100, soma(severidade + idadeSemanasLimitada + sensibilidade + reincidencia + decisao))", factors, nowUtc, Version);
    }
}

public sealed record CompletenessRequirement(string Code, decimal Weight, bool Required, bool Satisfied, bool Pending = false, bool NotApplicable = false, bool Waived = false);
public sealed record CompletenessScoreResult(decimal Score, IReadOnlyList<string> Missing, string Version);
public sealed class DocumentCompletenessCalculator
{
    public const string Version = "COMP-DET-2026-07";
    public CompletenessScoreResult Calculate(IEnumerable<CompletenessRequirement> requirements)
    {
        var applicable = requirements.Where(r => !r.NotApplicable && !r.Waived).ToArray();
        var total = applicable.Sum(r => r.Weight); if (total <= 0) return new(100, Array.Empty<string>(), Version);
        var done = applicable.Where(r => r.Satisfied).Sum(r => r.Weight);
        return new(Math.Round(done / total * 100, 2), applicable.Where(r => r.Required && !r.Satisfied && !r.Pending).Select(r => r.Code).ToArray(), Version);
    }
}
