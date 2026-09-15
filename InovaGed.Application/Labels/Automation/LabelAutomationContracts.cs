using System.Globalization;

namespace InovaGed.Application.Labels.Automation;

public enum LabelAutomationEventType { DocumentCreated, DocumentClassified, DocumentClassChanged, BoxCreated, BoxClosed, PhysicalMovement, BatchCreated, BatchStageChanged, Archived, Loaned, Returned, Collected, Disposed, AuthorizedManualRequest }
public enum LabelAutomationEntityType { Document, Box, Batch, PhysicalLocation, Manual }
public enum LabelAutomationExecutionMode { Draft, GenerateForApproval, GenerateAndQueue, GenerateAndAutoPrint, NotifyOnly }
public enum LabelConditionOperator { Equals, NotEquals, Contains, StartsWith, GreaterThan, LessThan, IsEmpty, IsNotEmpty }
public enum LabelConditionJoin { All, Any }

public sealed record LabelRuleCondition(string Field, LabelConditionOperator Operator, string? Value = null);
public sealed record LabelRuleConditionGroup(LabelConditionJoin Join, IReadOnlyList<LabelRuleCondition> Conditions);

public sealed record LabelAutomationRule(
    Guid Id, Guid TenantId, string Name, int Priority, LabelAutomationEventType Trigger,
    LabelAutomationEntityType EntityType, LabelRuleConditionGroup Conditions, Guid TemplateId,
    int PublishedVersion, Guid? PrintProfileId, int Copies, LabelAutomationExecutionMode Mode,
    bool RequiresApproval, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveUntil,
    bool Active, long Version, bool Deleted = false);

public sealed record LabelAutomationEvent(
    Guid TenantId, Guid EventId, LabelAutomationEventType Type, LabelAutomationEntityType EntityType,
    Guid EntityId, int Version, string CorrelationId, IReadOnlyDictionary<string, object?> Data,
    DateTimeOffset OccurredAt);

public sealed record LabelRuleMatch(LabelAutomationRule Rule, string IdempotencyKey, string Explanation);
public sealed record LabelRuleEvaluation(IReadOnlyList<LabelRuleMatch> Matches, IReadOnlyList<string> Warnings);
public sealed record LabelAutomationSimulation(int RecordsEvaluated, int RecordsMatched, int Labels,
    int Jobs, int EstimatedPages, IReadOnlyList<Guid> TemplateIds, IReadOnlyList<string> Warnings);

public interface ILabelAutomationRuleEngine
{
    LabelRuleEvaluation Evaluate(LabelAutomationEvent @event, IEnumerable<LabelAutomationRule> rules, bool automaticPrintingAllowed = false);
    LabelAutomationSimulation Simulate(LabelAutomationEvent @event, IEnumerable<LabelAutomationRule> rules);
}

public static class LabelAutomationIdempotency
{
    public static string Create(LabelAutomationEvent @event, LabelAutomationRule rule, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return string.Join(':', @event.TenantId.ToString("N"), rule.Id.ToString("N"), @event.EventId.ToString("N"),
            @event.EntityId.ToString("N"), @event.Version.ToString(CultureInfo.InvariantCulture), operation.Trim().ToUpperInvariant());
    }
}
