namespace InovaGed.Application.Auth;

public sealed class PasswordRecoveryUserDto
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ServidorId { get; set; }

    public string NomeUsuario { get; set; } = "";
    public string EmailUsuario { get; set; } = "";
    public string Cpf { get; set; } = "";
    public bool IsActive { get; set; }
}