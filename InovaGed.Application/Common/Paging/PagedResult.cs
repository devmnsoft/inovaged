namespace InovaGed.Application.Common.Paging;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);