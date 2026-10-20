using System;
using System.Threading;
using System.Threading.Tasks;

namespace InovaGed.Application.Security.Users;

public sealed class AppUserByCpfDto
{
    public AppUserByCpfDto() { }
    public AppUserByCpfDto(Guid Id, Guid TenantId, string Name, string Email, bool IsActive, string Cpf)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.Name = Name;
        this.Email = Email;
        this.IsActive = IsActive;
        this.Cpf = Cpf;
    }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public string Cpf { get; set; } = "";
}

public interface IAppUserRepository
{
    Task<AppUserByCpfDto?> GetByCpfAsync(Guid tenantId, string cpf, CancellationToken ct);
    Task<AppUserByCpfDto?> GetUniqueByCpfAsync(string cpf, CancellationToken ct);
}
