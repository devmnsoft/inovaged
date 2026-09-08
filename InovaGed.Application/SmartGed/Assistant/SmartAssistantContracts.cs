namespace InovaGed.Application.SmartGed.Assistant;

public interface ISmartGedAssistantService
{
 Task<SmartAssistantSessionDetails> StartSessionAsync(Guid tenantId, Guid userId, string? title, CancellationToken ct);
 Task<SmartAssistantAnswer> AskAsync(SmartAssistantQuestionCommand command, CancellationToken ct);
 Task<SmartAssistantSessionDetails?> GetSessionAsync(Guid tenantId, Guid userId, Guid sessionId, CancellationToken ct);
 Task<IReadOnlyList<SmartAssistantSessionItem>> ListSessionsAsync(Guid tenantId, Guid userId, CancellationToken ct);
 Task<IReadOnlyList<SmartAssistantCitation>> GetCitationsAsync(Guid tenantId, Guid userId, Guid messageId, CancellationToken ct);
 Task ReviewActionAsync(Guid tenantId, Guid actionId, Guid userId, bool accept, string? notes, CancellationToken ct);
}
public interface ISmartAssistantRetrievalService { Task<SmartAssistantRetrievalResult> RetrieveAsync(SmartAssistantRetrievalQuery query, CancellationToken ct); }
public interface ISmartAssistantAnswerComposer { Task<SmartAssistantAnswerDraft> ComposeAsync(SmartAssistantAnswerInput input, CancellationToken ct); }
public interface ISmartAssistantIntentResolver { SmartAssistantIntentResult Resolve(string question,IReadOnlyList<SmartAssistantConversationMessage> history); }
public interface ISmartAssistantEvidenceRanker { IReadOnlyList<SmartAssistantEvidence> Rank(string question,IEnumerable<SmartAssistantEvidence> evidence,int limit); }
public sealed record SmartAssistantQuestionCommand(Guid TenantId, Guid UserId, Guid SessionId, string Question);
public sealed record SmartAssistantConversationMessage(string Role,string Content);
public sealed record SmartAssistantIntentResult(string Intent,IReadOnlyDictionary<string,string> Filters,decimal Confidence);
public sealed record SmartAssistantRetrievalQuery(Guid TenantId, string Question, int Limit = 8,Guid? UserId=null,Guid? SessionId=null,IReadOnlyList<SmartAssistantConversationMessage>? ConversationHistory=null,SmartAssistantIntentResult? ResolvedIntent=null,int MaxExcerptCharacters=600);
public sealed record SmartAssistantEvidence(string SourceType, Guid? SourceId, string Title, string Excerpt, string? Url, decimal Confidence,IReadOnlyDictionary<string,string>? Facts=null,string? RankReason=null);
public sealed record SmartAssistantRetrievalResult(IReadOnlyList<SmartAssistantEvidence> Evidence, IReadOnlyList<string> Warnings,string ResolvedIntent="GENERAL_GED_SEARCH",long DurationMs=0);
public sealed record SmartAssistantAnswerInput(string Question, SmartAssistantRetrievalResult Retrieval,SmartAssistantIntentResult? Intent=null,IReadOnlyList<SmartAssistantConversationMessage>? ConversationHistory=null);
public sealed record SmartAssistantActionDraft(string ActionType, string Title, string? Description, string? TargetType, Guid? TargetId,string? Url=null);
public sealed record SmartAssistantAnswerDraft(string Content, decimal Confidence, string Status, IReadOnlyList<SmartAssistantActionDraft> Actions, IReadOnlyList<string> Warnings);
public sealed record SmartAssistantCitation(Guid Id, string SourceType, Guid? SourceId, string Title, string Excerpt, string? Url, decimal Confidence);
public sealed record SmartAssistantAction(Guid Id, string ActionType, string Title, string? Description, string Status,string? Url=null);
public sealed record SmartAssistantMessage(Guid Id, string Role, string Content, decimal Confidence, string Status, DateTimeOffset CreatedAt, IReadOnlyList<SmartAssistantCitation> Citations, IReadOnlyList<SmartAssistantAction> Actions);
public sealed record SmartAssistantAnswer(Guid MessageId, string Content, decimal Confidence, string Status, IReadOnlyList<SmartAssistantCitation> Citations, IReadOnlyList<SmartAssistantAction> Actions, IReadOnlyList<string> Warnings);
public sealed record SmartAssistantSessionItem(Guid Id, string Title, string Status, DateTimeOffset CreatedAt, DateTimeOffset? LastMessageAt);
public sealed record SmartAssistantSessionDetails(Guid Id, string Title, string Status, DateTimeOffset CreatedAt, IReadOnlyList<SmartAssistantMessage> Messages);
