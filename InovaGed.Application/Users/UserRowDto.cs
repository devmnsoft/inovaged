namespace InovaGed.Application.Users;

public sealed class UserRowDto
{
    public Guid Id { get; set; }
    public Guid? ServidorId { get; set; }

    public string Name { get; set; } = "";
    public string? Cpf { get; set; }
    public string? Matricula { get; set; }
    public string? Cargo { get; set; }
    public string? Funcao { get; set; }
    public string? Setor { get; set; }
    public string? Lotacao { get; set; }
    public string? Unidade { get; set; }

    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsLocked { get; set; }
    public bool MustChangePassword { get; set; }
    public bool MfaEnabled { get; set; }
    public bool CertificateRequired { get; set; }
    public bool CanSignWithIcp { get; set; }
    public string SecurityLevel { get; set; } = "PUBLIC";

    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public string RolesCsv { get; set; } = "";
}