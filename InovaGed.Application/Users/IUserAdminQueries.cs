namespace InovaGed.Application.Users;

public interface IUserAdminQueries
{
    Task<PagedResult<UserRowDto>> ListUsersAsync(
        Guid tenantId, string? q, bool? active,
        int page, int pageSize, CancellationToken ct);
}
