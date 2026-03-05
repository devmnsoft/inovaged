namespace InovaGed.Application.Ged.Users;

public class UserCertificateDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Cpf { get; set; } = "";

    public string Thumbprint { get; set; } = "";

    public string? SubjectDn { get; set; }

    public string? IssuerDn { get; set; }

    public string? SerialNumber { get; set; }

    public DateTime? NotBefore { get; set; }

    public DateTime? NotAfter { get; set; }

    public bool IsActive { get; set; }
}