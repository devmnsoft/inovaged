namespace InovaGed.Web.Models.Users;

public sealed class UserListVM
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool Locked { get; set; }
}
