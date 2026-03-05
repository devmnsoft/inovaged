namespace InovaGed.Application.Common.Security;

public interface ITenantProvider
{
    string? TenantId { get; }
}