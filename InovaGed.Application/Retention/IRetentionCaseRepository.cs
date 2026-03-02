namespace InovaGed.Application.RetentionCases;

public interface IRetentionCaseRepository
{
    Task<IReadOnlyList<RetentionCaseRow>> ListAsync(Guid tenantId, string? status, CancellationToken ct);
    Task<(RetentionCaseRow Case, IReadOnlyList<RetentionCaseItemRow> Items)?> GetAsync(Guid tenantId, Guid caseId, CancellationToken ct);

    Task<Guid> CreateAsync(Guid tenantId, Guid userId, CreateRetentionCaseRequest req, CancellationToken ct);

    Task DecideItemAsync(Guid tenantId, Guid userId, DecideItemRequest req, CancellationToken ct);

    Task CloseCaseAsync(Guid tenantId, Guid userId, Guid caseId, string newStatus, CancellationToken ct);

    Task<Guid> CreateFromQueueAsync(
     Guid tenantId,
     Guid? userId,
     string? userDisplay,
     Guid[] queueIds,
     string? title,
     string? notes,
     CancellationToken ct);

}