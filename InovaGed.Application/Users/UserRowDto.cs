namespace InovaGed.Application.Users;

public sealed class UserRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string RolesCsv { get; set; } = ""; // "Admin,Operador"
}
