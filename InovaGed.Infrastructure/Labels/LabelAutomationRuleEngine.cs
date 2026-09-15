using System.Globalization;
using InovaGed.Application.Labels.Automation;

namespace InovaGed.Infrastructure.Labels;

/// <summary>Canonical, side-effect-free authority for selecting label automation rules.</summary>
public sealed class LabelAutomationRuleEngine : ILabelAutomationRuleEngine
{
    private const int MaximumCopies = 100;

    public LabelRuleEvaluation Evaluate(LabelAutomationEvent @event, IEnumerable<LabelAutomationRule> rules, bool automaticPrintingAllowed = false)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(rules);
        if (@event.TenantId == Guid.Empty || @event.EventId == Guid.Empty || @event.EntityId == Guid.Empty)
            throw new ArgumentException("Tenant, evento e entidade são obrigatórios.", nameof(@event));

        var warnings = new List<string>();
        var matches = new List<LabelRuleMatch>();
        foreach (var rule in rules.OrderByDescending(x => x.Priority).ThenBy(x => x.Id))
        {
            Validate(rule);
            if (rule.TenantId != @event.TenantId || rule.Deleted || !rule.Active || rule.Trigger != @event.Type || rule.EntityType != @event.EntityType)
                continue;
            if (@event.OccurredAt < rule.EffectiveFrom || (rule.EffectiveUntil is not null && @event.OccurredAt > rule.EffectiveUntil))
                continue;
            if (!Matches(rule.Conditions, @event.Data)) continue;
            if (rule.Mode == LabelAutomationExecutionMode.GenerateAndAutoPrint && !automaticPrintingAllowed)
            {
                warnings.Add($"Regra '{rule.Name}' requer permissão específica; execução automática foi bloqueada.");
                continue;
            }
            matches.Add(new LabelRuleMatch(rule, LabelAutomationIdempotency.Create(@event, rule, "GENERATE"), Explain(rule)));
        }
        if (matches.Count > 1 && matches[0].Rule.Priority == matches[1].Rule.Priority)
            warnings.Add("Há regras compatíveis com a mesma prioridade; a ordem estável por ID foi aplicada.");
        return new(matches, warnings);
    }

    public LabelAutomationSimulation Simulate(LabelAutomationEvent @event, IEnumerable<LabelAutomationRule> rules)
    {
        // Deliberately evaluates in memory and never invokes repositories, sequences, renderers or print services.
        var materialized = rules.ToArray();
        var result = Evaluate(@event, materialized, automaticPrintingAllowed: false);
        var actionable = result.Matches.Where(x => x.Rule.Mode != LabelAutomationExecutionMode.NotifyOnly).ToArray();
        var labels = actionable.Sum(x => x.Rule.Copies);
        var jobs = actionable.Count(x => x.Rule.Mode is LabelAutomationExecutionMode.GenerateAndQueue or LabelAutomationExecutionMode.GenerateAndAutoPrint);
        return new(1, result.Matches.Count, labels, jobs, labels, actionable.Select(x => x.Rule.TemplateId).Distinct().ToArray(), result.Warnings);
    }

    private static bool Matches(LabelRuleConditionGroup group, IReadOnlyDictionary<string, object?> data)
    {
        if (group.Conditions.Count == 0) return false; // prevent accidentally broad rules
        var results = group.Conditions.Select(condition => Match(condition, data));
        return group.Join == LabelConditionJoin.All ? results.All(x => x) : results.Any(x => x);
    }

    private static bool Match(LabelRuleCondition condition, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || condition.Field.Contains(';') || condition.Field.Contains("--", StringComparison.Ordinal)) return false;
        data.TryGetValue(condition.Field, out var raw);
        var actual = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
        var expected = condition.Value?.Trim();
        return condition.Operator switch
        {
            LabelConditionOperator.IsEmpty => string.IsNullOrEmpty(actual),
            LabelConditionOperator.IsNotEmpty => !string.IsNullOrEmpty(actual),
            LabelConditionOperator.Equals => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            LabelConditionOperator.NotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            LabelConditionOperator.Contains => actual?.Contains(expected ?? "", StringComparison.OrdinalIgnoreCase) == true,
            LabelConditionOperator.StartsWith => actual?.StartsWith(expected ?? "", StringComparison.OrdinalIgnoreCase) == true,
            LabelConditionOperator.GreaterThan => Compare(actual, expected) > 0,
            LabelConditionOperator.LessThan => Compare(actual, expected) < 0,
            _ => false
        };
    }

    private static int Compare(string? left, string? right)
    {
        if (decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) && decimal.TryParse(right, NumberStyles.Number, CultureInfo.InvariantCulture, out var b)) return a.CompareTo(b);
        if (DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ad) && DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var bd)) return ad.CompareTo(bd);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void Validate(LabelAutomationRule rule)
    {
        if (rule.Id == Guid.Empty || rule.TenantId == Guid.Empty || rule.TemplateId == Guid.Empty) throw new ArgumentException("Regra, tenant e modelo são obrigatórios.", nameof(rule));
        if (string.IsNullOrWhiteSpace(rule.Name) || rule.Copies is < 1 or > MaximumCopies || rule.PublishedVersion < 1 || rule.Version < 1) throw new ArgumentException("Regra de automação inválida.", nameof(rule));
        if (rule.EffectiveUntil < rule.EffectiveFrom) throw new ArgumentException("Vigência da regra inválida.", nameof(rule));
    }

    private static string Explain(LabelAutomationRule rule) =>
        $"Quando {rule.EntityType} receber o evento {rule.Trigger}, gerar {rule.Copies} cópia(s) da versão publicada {rule.PublishedVersion} e executar {rule.Mode}.";
}
