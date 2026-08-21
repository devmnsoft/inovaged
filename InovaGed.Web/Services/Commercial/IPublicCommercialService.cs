using InovaGed.Web.Models.Commercial;

namespace InovaGed.Web.Services.Commercial;

public interface IPublicCommercialService
{
    Task<IReadOnlyList<PublicPlan>> GetPlansAsync(CancellationToken ct);
    Task<SignupResult> SignupAsync(SignupViewModel model, string passwordHash, string? ip, CancellationToken ct);
    Task<Guid> CreateLeadAsync(DemoRequestViewModel model, string? ip, CancellationToken ct);
    Task<string?> CreatePasswordResetAsync(string email, string? ip, CancellationToken ct);
    Task<bool> ResetPasswordAsync(string token, string passwordHash, CancellationToken ct);
    Task<IReadOnlyList<CommercialLead>> GetLeadsAsync(string? status, CancellationToken ct);
    Task UpdateLeadAsync(Guid id, string status, string? note, Guid? userId, CancellationToken ct);
}
