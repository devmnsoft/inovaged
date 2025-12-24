using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Auth;

public sealed class LoginVM
{
    [Required(ErrorMessage = "Informe o Tenant (slug).")]
    [Display(Name = "Tenant (slug)")]
    public string TenantSlug { get; set; } = "default";

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Informe a senha.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
