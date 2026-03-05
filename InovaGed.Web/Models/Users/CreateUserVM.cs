using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Users;

public sealed class CreateUserVM
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = "";

    [Required, MinLength(8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required, Compare(nameof(Password))]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";

    public bool IsActive { get; set; } = true;

    // Multi-select (binder OK)
    public List<Guid> SelectedRoleIds { get; set; } = new();

    // Para popular a tela
    public List<RoleItem> AvailableRoles { get; set; } = new();

    public sealed class RoleItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }
}