using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Commercial;

public sealed record PublicPlan(string Code, string Name, decimal MonthlyPrice, int Diagnostics, int Responses,
    string Reports, string Certificates, string Intelligence, string Benchmark, string Units, string Users,
    string Integrations, string Support, bool IsActive = true);

public sealed class SignupViewModel
{
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(180)] public string Email { get; set; } = "";
    [Required, Phone, StringLength(30)] public string Phone { get; set; } = "";
    [Required, StringLength(80)] public string JobTitle { get; set; } = "";
    [Required, MinLength(12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, StringLength(160)] public string OrganizationName { get; set; } = "";
    [StringLength(18)] public string? Cnpj { get; set; }
    [Required] public string PlanCode { get; set; } = "START";
    [Range(typeof(bool), "true", "true", ErrorMessage = "É necessário aceitar os termos de uso.")]
    public bool AcceptTerms { get; set; }
    public string? Website { get; set; }
}

public sealed class DemoRequestViewModel
{
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [Required, StringLength(160)] public string Company { get; set; } = "";
    [Required, EmailAddress, StringLength(180)] public string Email { get; set; } = "";
    [Required, Phone, StringLength(30)] public string Phone { get; set; } = "";
    [Required, StringLength(80)] public string JobTitle { get; set; } = "";
    [Required, StringLength(40)] public string CompanySize { get; set; } = "";
    [Required, StringLength(80)] public string MainInterest { get; set; } = "";
    [StringLength(1200)] public string? Message { get; set; }
    public string? Website { get; set; }
}

public sealed class PasswordRequestViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
}

public sealed class PasswordResetViewModel
{
    [Required] public string Token { get; set; } = "";
    [Required, MinLength(12), DataType(DataType.Password)] public string Password { get; set; } = "";
}

public sealed record SignupResult(Guid OrganizationId, Guid UserId, DateTimeOffset TrialEndsAt, string TenantCode);
public sealed record CommercialLead(Guid Id, string Name, string Company, string Email, string Status, string Origin, DateTimeOffset CreatedAt);
