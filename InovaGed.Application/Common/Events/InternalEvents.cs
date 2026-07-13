namespace InovaGed.Application.Common.Events;

public interface IInternalEvent
{
    Guid TenantId { get; }
    Guid? DocumentId { get; }
    string EventType { get; }
    string CorrelationId { get; }
    DateTime OccurredAtUtc { get; }
}

public sealed record DocumentLifecycleEvent(Guid TenantId, Guid? DocumentId, string EventType, string CorrelationId, DateTime OccurredAtUtc, IReadOnlyDictionary<string, string?> Data) : IInternalEvent;

public static class InternalEventTypes
{
    public const string DocumentUploaded = nameof(DocumentUploaded);
    public const string DocumentVersionCreated = nameof(DocumentVersionCreated);
    public const string OcrCompleted = nameof(OcrCompleted);
    public const string PreviewCompleted = nameof(PreviewCompleted);
    public const string DocumentClassified = nameof(DocumentClassified);
    public const string DocumentMoved = nameof(DocumentMoved);
    public const string DocumentSigned = nameof(DocumentSigned);
    public const string LoanCreated = nameof(LoanCreated);
    public const string LoanReturned = nameof(LoanReturned);
    public const string RetentionChanged = nameof(RetentionChanged);
    public const string AccessDenied = nameof(AccessDenied);
    public const string DocumentViewed = nameof(DocumentViewed);
    public const string DocumentDownloaded = nameof(DocumentDownloaded);
}

public interface IOutboxWriter
{
    Task EnqueueAsync(IInternalEvent evt, CancellationToken ct);
}
