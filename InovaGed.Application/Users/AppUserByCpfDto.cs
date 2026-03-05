using System;
using System.Threading;
using System.Threading.Tasks;

namespace InovaGed.Application.Security.Users;

public sealed record AppUserByCpfDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Email,
    bool IsActive,
    string Cpf
);

public interface IAppUserRepository
{
    Task<AppUserByCpfDto?> GetByCpfAsync(Guid tenantId, string cpf, CancellationToken ct);
}