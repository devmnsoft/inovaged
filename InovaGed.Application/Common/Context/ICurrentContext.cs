namespace InovaGed.Application.Common.Context;

public interface ICurrentContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    string? UserEmail { get; }
    bool IsAuthenticated { get; }
    string? UserDisplay { get; }
}