using InovaGed.Application.Labels.Automation;
using InovaGed.Infrastructure.Labels;

namespace InovaGed.Application.Tests;

public sealed class LabelAutomationRc46Tests
{
    private static readonly Guid Tenant = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Entity = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Selects_only_same_tenant_and_orders_by_priority()
    {
        var low = Rule(priority: 1);
        var high = Rule(priority: 10);
        var foreign = Rule(priority: 50) with { TenantId = Guid.Parse("90000000-0000-0000-0000-000000000001") };
        var result = new LabelAutomationRuleEngine().Evaluate(Event(), [low, foreign, high]);
        Assert.Equal([high.Id, low.Id], result.Matches.Select(x => x.Rule.Id));
    }

    [Fact]
    public void Duplicate_event_produces_same_idempotency_key()
    {
        var engine = new LabelAutomationRuleEngine(); var rule = Rule(); var occurrence = Event();
        Assert.Equal(engine.Evaluate(occurrence, [rule]).Matches[0].IdempotencyKey,
            engine.Evaluate(occurrence, [rule]).Matches[0].IdempotencyKey);
    }

    [Fact]
    public void Automatic_printing_is_denied_by_default()
    {
        var rule = Rule() with { Mode = LabelAutomationExecutionMode.GenerateAndAutoPrint };
        var result = new LabelAutomationRuleEngine().Evaluate(Event(), [rule]);
        Assert.Empty(result.Matches); Assert.Single(result.Warnings);
    }

    [Fact]
    public void Simulation_is_deterministic_and_has_no_mutation_contract()
    {
        var rule = Rule() with { Copies = 2, Mode = LabelAutomationExecutionMode.GenerateAndQueue };
        var rules = new[] { rule };
        var result = new LabelAutomationRuleEngine().Simulate(Event(), rules);
        Assert.Equal(1, result.RecordsMatched); Assert.Equal(2, result.Labels); Assert.Equal(1, result.Jobs);
        Assert.Same(rule, rules[0]);
    }

    [Fact]
    public void Empty_conditions_do_not_create_broad_automation()
    {
        var rule = Rule() with { Conditions = new(LabelConditionJoin.All, []) };
        Assert.Empty(new LabelAutomationRuleEngine().Evaluate(Event(), [rule]).Matches);
    }

    private static LabelAutomationEvent Event() => new(Tenant, Guid.Parse("30000000-0000-0000-0000-000000000001"),
        LabelAutomationEventType.BoxClosed, LabelAutomationEntityType.Box, Entity, 3, "corr-46",
        new Dictionary<string, object?> { ["department"] = "Arquivo Médico", ["confidential"] = true }, Now);

    private static LabelAutomationRule Rule(int priority = 1) => new(Guid.Parse($"50000000-0000-0000-0000-{priority:D12}"), Tenant, "Caixa médica", priority,
        LabelAutomationEventType.BoxClosed, LabelAutomationEntityType.Box,
        new(LabelConditionJoin.All, [new("department", LabelConditionOperator.Equals, "Arquivo Médico")]),
        Guid.Parse("40000000-0000-0000-0000-000000000001"), 2, null, 1,
        LabelAutomationExecutionMode.GenerateForApproval, true, Now.AddDays(-1), null, true, 1);
}
