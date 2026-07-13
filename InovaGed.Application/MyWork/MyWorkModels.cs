namespace InovaGed.Application.MyWork;
public sealed record MyWorkItemDto(Guid Id, Guid TenantId, string Source, string Type, string Title, string Priority, DateTime? DueAtUtc, Guid? ResponsibleUserId, string Status, string MainAction, string Link);
public interface IMyWorkQueryService { Task<IReadOnlyList<MyWorkItemDto>> GetAsync(Guid tenantId, Guid userId, int limit, CancellationToken ct); }
